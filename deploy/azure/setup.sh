#!/usr/bin/env bash
# One-time setup that lets this repository's GitHub Actions deploy 4C CMMS into your Azure subscription.
#
# Run it in Azure Cloud Shell (Bash) as an Owner of the subscription:
#   curl -fsSLO https://raw.githubusercontent.com/MoXFahmy/CMMSX/main/deploy/azure/setup.sh
#   bash setup.sh
#
# Optional settings (environment variables):
#   LOCATION=uaenorth          Azure region for everything
#   RESOURCE_GROUP=rg-cmms-test
#   REPO=MoXFahmy/CMMSX        GitHub repository allowed to deploy
#
# It is safe to run again: existing items are reused.
set -euo pipefail

REPO="${REPO:-MoXFahmy/CMMSX}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-cmms-test}"
LOCATION="${LOCATION:-uaenorth}"
DEPLOYER_NAME="${DEPLOYER_NAME:-github-${REPO//\//-}-deploy}"

# Built-in roles the deployment may hand to the app's own identity (and nothing else).
ACR_PULL=7f951dda-4ed3-4680-a7ca-43fe172d538d
STORAGE_BLOB_DATA_CONTRIBUTOR=ba92f5b4-2d11-453d-a403-e96b0029c9fe

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
echo "Subscription: $(az account show --query name -o tsv) ($SUBSCRIPTION_ID)"
echo "Region:       $LOCATION"
echo

echo "1/5 Registering the Azure services used (the first time this can take a few minutes)..."
for ns in Microsoft.App Microsoft.ContainerRegistry Microsoft.Sql Microsoft.Storage Microsoft.OperationalInsights Microsoft.ManagedIdentity; do
  az provider register --namespace "$ns" --wait -o none
done

echo "2/5 Creating resource group $RESOURCE_GROUP..."
RG_ID=$(az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --query id -o tsv)

echo "3/5 Creating the sign-in for GitHub Actions ($DEPLOYER_NAME)..."
CLIENT_ID=$(az ad app list --display-name "$DEPLOYER_NAME" --query '[0].appId' -o tsv)
if [ -z "$CLIENT_ID" ]; then
  CLIENT_ID=$(az ad app create --display-name "$DEPLOYER_NAME" --query appId -o tsv)
fi
SP_ID=$(az ad sp show --id "$CLIENT_ID" --query id -o tsv 2>/dev/null || true)
if [ -z "$SP_ID" ]; then
  SP_ID=$(az ad sp create --id "$CLIENT_ID" --query id -o tsv)
fi

# GitHub may sign in only from workflows running on this repository's main branch.
SUBJECT="repo:${REPO}:ref:refs/heads/main"
if [ -z "$(az ad app federated-credential list --id "$CLIENT_ID" --query "[?subject=='$SUBJECT'].name" -o tsv)" ]; then
  az ad app federated-credential create --id "$CLIENT_ID" -o none --parameters "{
    \"name\": \"github-main\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"$SUBJECT\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"
fi

echo "4/5 Allowing it to manage resource group $RESOURCE_GROUP only..."
assign() { # role, [condition]
  local existing extra=()
  existing=$(az role assignment list --scope "$RG_ID" --query "[?principalId=='$SP_ID' && roleDefinitionName=='$1'].id" -o tsv)
  [ -n "$existing" ] && return 0
  [ -n "${2:-}" ] && extra=(--condition "$2" --condition-version 2.0)
  # A brand-new sign-in can take a minute to become visible to Azure role assignments; retry briefly.
  for attempt in 1 2 3 4 5 6; do
    if az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
         --role "$1" --scope "$RG_ID" "${extra[@]}" -o none; then
      return 0
    fi
    echo "    (waiting for the new sign-in to replicate, attempt $attempt)"; sleep 15
  done
  return 1
}
assign "Contributor"
# Role assignments limited to the two roles the app needs: pull its image, read/write attachments.
assign "Role Based Access Control Administrator" "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ACR_PULL, $STORAGE_BLOB_DATA_CONTRIBUTOR})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ACR_PULL, $STORAGE_BLOB_DATA_CONTRIBUTOR}))"

echo "5/5 Connecting GitHub..."
if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  gh variable set AZURE_CLIENT_ID --repo "$REPO" --body "$CLIENT_ID"
  gh variable set AZURE_TENANT_ID --repo "$REPO" --body "$TENANT_ID"
  gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$SUBSCRIPTION_ID"
  gh variable set AZURE_RESOURCE_GROUP --repo "$REPO" --body "$RESOURCE_GROUP"
  echo "    GitHub repository variables set."
  echo
  echo "Done. Start the first deployment here (Run workflow):"
else
  cat <<EOF

Done. In GitHub, add these four repository variables:
  https://github.com/$REPO/settings/variables/actions  ->  New repository variable

  Name                    Value
  AZURE_CLIENT_ID         $CLIENT_ID
  AZURE_TENANT_ID         $TENANT_ID
  AZURE_SUBSCRIPTION_ID   $SUBSCRIPTION_ID
  AZURE_RESOURCE_GROUP    $RESOURCE_GROUP

Then start the first deployment here (Run workflow):
EOF
fi
echo "  https://github.com/$REPO/actions/workflows/deploy-azure.yml"
