// 4C CMMS on Azure: one container app (web + API), Azure SQL Database and Blob Storage for attachments.
// The app signs in to SQL and Storage with its own managed identity, so there are no passwords or keys to manage.
// Deployed by .github/workflows/deploy-azure.yml; see docs/DEPLOY_AZURE.md.
targetScope = 'resourceGroup'

@description('Short name used for the container app (and so the web address) and in resource names.')
@minLength(2)
@maxLength(20)
param appName string = 'cmms'

@description('Azure region. Defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('Container image to run, e.g. crxxxx.azurecr.io/cmms:<commit>. Empty = create or update the infrastructure only.')
param image string = ''

@description('Azure SQL Database size: Basic = 5 DTU, S0 = 10, S1 = 20, S2 = 50, S3 = 100. Can be changed later without downtime.')
@allowed(['Basic', 'S0', 'S1', 'S2', 'S3'])
param databaseSku string = 'S1'

@description('Let visitors create new organizations from the sign-in page.')
param allowRegistration bool = true

@description('Tags applied to every resource.')
param tags object = { application: '4c-cmms' }

var suffix = uniqueString(resourceGroup().id)
var name = toLower(appName)

// Built-in roles the app's identity needs.
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

// ---------- Identity ----------

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}-${suffix}'
  location: location
  tags: tags
}

// ---------- Container registry ----------

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${suffix}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, roles.acrPull)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Logs ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ---------- Attachments (Blob Storage) ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${suffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // identity-based access only
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 14 }
    containerDeleteRetentionPolicy: { enabled: true, days: 14 }
  }
}

resource filesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'files'
  properties: {
    publicAccess: 'None'
  }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, roles.storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Database (Azure SQL) ----------

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql-${name}-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // The app's identity is the server's Microsoft Entra admin, so it can apply schema migrations on start-up.
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: identity.name
      sid: identity.properties.principalId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// Lets Azure services (the container app) reach the server; access still requires Microsoft Entra sign-in.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: 'cmms'
  location: location
  tags: tags
  sku: {
    name: databaseSku
    tier: databaseSku == 'Basic' ? 'Basic' : 'Standard'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

// ---------- Container Apps ----------

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${name}-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

var healthProbe = {
  path: '/api/health'
  port: 8080
}

resource app 'Microsoft.App/containerApps@2024-03-01' = if (!empty(image)) {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [
    acrPull
    blobContributor
    allowAzureServices
  ]
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false // HTTP is redirected to HTTPS
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'cmms'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            // TLS ends at the Container Apps front end; trust its X-Forwarded-* headers so cookies are marked Secure.
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            {
              name: 'ConnectionStrings__Cmms'
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60'
            }
            { name: 'Database__MigrateOnStartup', value: 'true' }
            { name: 'Storage__Provider', value: 'AzureBlob' }
            { name: 'Storage__BlobEndpoint', value: storage.properties.primaryEndpoints.blob }
            { name: 'Storage__Container', value: filesContainer.name }
            { name: 'Azure__ClientId', value: identity.properties.clientId }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
            { name: 'Auth__AllowRegistration', value: string(allowRegistration) }
          ]
          probes: [
            {
              // Schema migrations run before the app starts listening; allow a few minutes on the first start.
              type: 'Startup'
              httpGet: healthProbe
              initialDelaySeconds: 10
              periodSeconds: 20
              timeoutSeconds: 5
              failureThreshold: 10
            }
            {
              type: 'Liveness'
              httpGet: healthProbe
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: healthProbe
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
        }
      ]
      // One replica: the PM scheduler and start-up migrations assume a single instance.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output sqlServerName string = sqlServer.name
output databaseName string = database.name
output storageAccountName string = storage.name
output identityName string = identity.name
output containerAppName string = name
// The app exists whenever an image is given.
#disable-next-line BCP318
output url string = empty(image) ? '' : 'https://${app.properties.configuration.ingress.fqdn}'
