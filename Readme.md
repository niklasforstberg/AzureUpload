Do not forget to set the environment variables in the .env file, or locally use secrets:

dotnet user-secrets set "AzureStorage:ContainerName" "your-container-name"
dotnet user-secrets set "AzureStorage:ConnectionString" "your-connection-string"
dotnet user-secrets set "AzureStorage:Key" "your-key"
dotnet user-secrets set "Jwt:Key" "your-secret-key"
dotnet user-secrets set "Jwt:Issuer" "your-issuer"
dotnet user-secrets set "Jwt:Audience" "your-audience"


To create the initial admin user:
curl -X POST http://localhost:5000/api/auth/register-admin \
-H "Content-Type: application/json" \
-d '{"username":"admin","password":"your-secure-password","role":"Admin"}'


TODO

