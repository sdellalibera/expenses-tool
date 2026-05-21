# Content Understanding Demo

## Configuration

Before running the Aspire AppHost, configure the required Azure settings and parameters using `aspire secret set` from the `src/aspire` directory.

```bash
cd src/aspire

# Azure deployment settings
aspire secret set Azure:SubscriptionId "<your-subscription-id>"
aspire secret set Azure:ResourceGroup "<your-resource-group>"
aspire secret set Azure:Location "<your-azure-region>"

# Existing Foundry resource to reference
aspire secret set Parameters:existingFoundryName "<your-foundry-name>"
aspire secret set Parameters:existingFoundryResourceGroup "<foundry-resource-group>"


