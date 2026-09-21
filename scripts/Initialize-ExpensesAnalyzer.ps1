[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri] $ContentUnderstandingEndpoint,
    [string] $AnalyzerPath = (Join-Path $PSScriptRoot '../src/analyzers/Expenses.json'),
    [string] $AnalyzerId = 'ExpensesAnalyzer',
    [string] $CompletionDeployment = 'gpt5-4',
    [string] $MiniDeployment = 'gpt5-4-mini',
    [string] $EmbeddingDeployment = 'TextEmbedding3Large',
    [ValidateRange(1, 3600)]
    [int] $TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'
Set-StrictMode -Version Latest

if (-not $ContentUnderstandingEndpoint.IsAbsoluteUri -or
    $ContentUnderstandingEndpoint.Scheme -ne 'https' -or
    $ContentUnderstandingEndpoint.AbsolutePath.Trim('/') -ne '' -or
    $ContentUnderstandingEndpoint.Query -or $ContentUnderstandingEndpoint.Fragment -or
    $ContentUnderstandingEndpoint.UserInfo) {
    throw 'Use the HTTPS Content Understanding account endpoint, not a Foundry project endpoint.'
}

$null = Get-Command az -ErrorAction Stop
$token = az account get-access-token --scope 'https://cognitiveservices.azure.com/.default' --query accessToken --output tsv --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'Could not obtain a Content Understanding token. Run az login before starting Aspire.'
}

$headers = @{ Authorization = "Bearer $($token.Trim())" }
$baseUri = "$($ContentUnderstandingEndpoint.AbsoluteUri.TrimEnd('/'))/contentunderstanding"
$apiVersion = '2025-11-01'
$analyzerPathSegment = "analyzers/$([uri]::EscapeDataString($AnalyzerId))?api-version=$apiVersion"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

function Invoke-ContentUnderstandingRequest {
    param(
        [string] $Method,
        [string] $Path,
        [string] $Body,
        [string] $ContentType = 'application/json',
        [int[]] $AllowedStatusCodes = @()
    )

    while ((Get-Date) -lt $deadline) {
        $remainingSeconds = [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
        $statusCode = 0
        $request = @{
            Uri = "$baseUri/$Path"
            Method = $Method
            Headers = $headers
            SkipHttpErrorCheck = $true
            StatusCodeVariable = 'statusCode'
            TimeoutSec = [Math]::Max(1, [Math]::Min(30, $remainingSeconds))
        }
        if ($Body) {
            $request.Body = [System.Text.Encoding]::UTF8.GetBytes($Body)
            $request.ContentType = $ContentType
        }
        $response = Invoke-RestMethod @request
        if (($statusCode -ge 200 -and $statusCode -lt 300) -or $statusCode -in $AllowedStatusCodes) {
            return [pscustomobject]@{ StatusCode = $statusCode; Body = $response }
        }
        if ($statusCode -notin @(403, 408, 429, 500, 502, 503, 504)) {
            $details = ''
            if ($null -ne $response) {
                $errorProperty = $response.PSObject.Properties['error']
                if ($errorProperty) {
                    $details = $errorProperty.Value | ConvertTo-Json -Depth 10 -Compress
                }
            }
            throw "Content Understanding $Method $Path failed (HTTP $statusCode). $details"
        }
        Write-Information "Content Understanding returned HTTP $statusCode; retrying."
        Start-Sleep -Seconds 2
    }
    throw "Content Understanding setup timed out after $TimeoutSeconds seconds. Check endpoint access, model deployments and Cognitive Services User permissions."
}

$existing = Invoke-ContentUnderstandingRequest -Method GET -Path $analyzerPathSegment -AllowedStatusCodes 404
if ($existing.StatusCode -eq 200) {
    Write-Output "Analyzer already exists: $AnalyzerId."
    return
}

$definition = Get-Content -LiteralPath $AnalyzerPath -Raw
$parsedDefinition = $definition | ConvertFrom-Json -AsHashtable
if (-not $parsedDefinition.ContainsKey('fieldSchema') -or -not $parsedDefinition.ContainsKey('models')) {
    throw "Invalid analyzer definition: $AnalyzerPath. Expected fieldSchema and models."
}

$requiredMappings = @{
    'gpt-5.4' = $CompletionDeployment
    'gpt-5.4-mini' = $MiniDeployment
    'text-embedding-3-large' = $EmbeddingDeployment
    'prebuilt-analyzer-completion' = $CompletionDeployment
    'prebuilt-analyzer-completion-mini' = $MiniDeployment
    'prebuilt-analyzer-embedding' = $EmbeddingDeployment
}
$defaultsPath = "defaults?api-version=$apiVersion"
do {
    $defaults = Invoke-ContentUnderstandingRequest -Method GET -Path $defaultsPath
    $currentMappings = @{}
    $deploymentProperty = $defaults.Body.PSObject.Properties['modelDeployments']
    if ($deploymentProperty -and $deploymentProperty.Value) {
        foreach ($property in $deploymentProperty.Value.PSObject.Properties) {
            $currentMappings[$property.Name] = $property.Value
        }
    }
    $missingMappings = @{}
    foreach ($name in $requiredMappings.Keys) {
        if (-not $currentMappings[$name]) {
            $missingMappings[$name] = $requiredMappings[$name]
        }
    }
    if ($missingMappings.Count -eq 0) {
        break
    }
    $patchBody = @{ modelDeployments = $missingMappings } | ConvertTo-Json -Depth 10
    $null = Invoke-ContentUnderstandingRequest -Method PATCH -Path $defaultsPath -Body $patchBody `
        -ContentType 'application/merge-patch+json' -AllowedStatusCodes 409
    Start-Sleep -Seconds 2
} while ($true)

Write-Information "Creating analyzer '$AnalyzerId' from $AnalyzerPath."
$creation = Invoke-ContentUnderstandingRequest -Method PUT -Path "$analyzerPathSegment&allowReplace=false" `
    -Body $definition -AllowedStatusCodes 409
if ($creation.StatusCode -eq 409) {
    Write-Information "Analyzer '$AnalyzerId' was created by another process; waiting for readiness."
}

while ($true) {
    $analyzer = Invoke-ContentUnderstandingRequest -Method GET -Path $analyzerPathSegment -AllowedStatusCodes 404
    if ($analyzer.StatusCode -eq 200) {
        $status = [string] $analyzer.Body.status
        if ($status -eq 'ready') {
            Write-Output "Analyzer ready: $AnalyzerId."
            return
        }
        if ($status -notin @('creating', 'notStarted', 'running')) {
            throw "Analyzer '$AnalyzerId' creation failed (status: $status). Check its definition and model deployments."
        }
    }
    Start-Sleep -Seconds 2
}