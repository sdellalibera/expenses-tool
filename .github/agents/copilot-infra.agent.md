# Bicep Infrastructure Agent Instructions

## Purpose

These instructions guide the generation and review of Azure Bicep templates to avoid common deployment errors.

## Rules

### Parameters vs Variables

- Do NOT use `param` for resource names unless the caller genuinely needs to override them. Prefer `var` with hardcoded values for fixed environments.
- Never include a `param` that is only used once with a known value — make it a `var`.

### Read-Only Properties

- Never set `tier` inside a `sku` block on `Microsoft.Storage/storageAccounts`. It is read-only and will produce warning BCP073.
- Never set `sku` on `Microsoft.Storage/storageAccounts/blobServices` or `Microsoft.Storage/storageAccounts/fileServices`. The SKU is inherited from the parent storage account and is read-only on these child resources.

### Retention Policies

- When `enabled: false` on `shareDeleteRetentionPolicy`, do NOT set `days: 0`. The minimum allowed value is 1 and the property is irrelevant when disabled. Omit `days` entirely.

### dependsOn

- Do NOT add `dependsOn` referencing a grandparent resource when the resource already has a `parent` property pointing to a child of that grandparent. Bicep infers the dependency chain automatically through `parent`. Adding it explicitly triggers the `no-unnecessary-dependson` linter warning.

### Cognitive Services / AI Services

#### Soft-Delete and Restore

- Do NOT set `restore: true` unless you have confirmed a soft-deleted resource actually exists. If the resource was never created or has been purged, `restore: true` causes error `CanNotRestoreANonExistingResource`.
- If you need conditional restore, use a `param restoreCognitiveServices bool = false` and apply it with a condition or ternary — never hardcode `restore: true`.

#### RAI Policies

- Never include `Microsoft.CognitiveServices/accounts/raiPolicies` resources for system-managed policies. Both `Microsoft.Default` and `Microsoft.DefaultV2` are system policies that Azure manages automatically. Attempting to create or update them causes error: `Invalid rai policy. This is system policy which can't be updated.`
- Model deployments can still reference these policies via `raiPolicyName: 'Microsoft.DefaultV2'` — the policy exists on the account by default; it just cannot be declared in Bicep.

#### Sequential Child Deployments

- All child resources under a single `Microsoft.CognitiveServices/accounts` parent must be deployed sequentially. The Cognitive Services RP does not support concurrent operations on the same parent account.
- Chain child resources with explicit `dependsOn` to form a sequential deployment order. Example:

  ```bicep
  resource defenderSettings '...defenderForAISettings...' = { ... }

  resource gpt41 '...deployments...' = {
    ...
    dependsOn: [defenderSettings]
  }

  resource gpt41mini '...deployments...' = {
    ...
    dependsOn: [gpt41]
  }

  resource embedding '...deployments...' = {
    ...
    dependsOn: [gpt41mini]
  }
  ```

- This applies to all child resource types: `deployments`, `defenderForAISettings`, `raiPolicies` (custom ones only), etc.

### Event Grid System Topics

- When exporting existing Azure resources to Bicep, Event Grid system topics and their subscriptions may appear even if you never explicitly created them. Azure auto-creates system topics for certain resource types (e.g., Storage Accounts with Defender for Storage enabled).
- Review exported templates carefully and remove resources you do not intend to manage. Including auto-created resources can cause unexpected conflicts or require additional permissions.

### Outputs

- Always add `output` declarations for resource endpoints so they are visible after deployment.
- Cognitive Services endpoints: `resource.properties.endpoint`
- Storage account endpoints: `resource.properties.primaryEndpoints.blob`, `.file`, `.queue`, `.table`

## Template Review Checklist

Before generating or finalizing any Bicep template, verify:

1. [ ] No read-only properties are being set (`tier` on storage SKU, `sku` on blob/file services)
2. [ ] No `days: 0` on any retention policy — omit `days` when `enabled: false`
3. [ ] No unnecessary `dependsOn` where `parent` already implies the dependency
4. [ ] No system RAI policies (`Microsoft.Default`, `Microsoft.DefaultV2`) declared as resources
5. [ ] `restore: true` is NOT set unless a soft-deleted resource is confirmed to exist
6. [ ] All child resources of a Cognitive Services account are chained with `dependsOn`
7. [ ] No auto-generated resources (Event Grid system topics) unless intentionally managed
8. [ ] Deployment outputs are defined for all resource endpoints
