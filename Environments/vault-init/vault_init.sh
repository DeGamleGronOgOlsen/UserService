export VAULT_ADDR='https://vaulthost:8201'
export VAULT_TOKEN='00000000-0000-0000-0000-000000000000'

# give some time for Vault to start and be ready
sleep 10

# Insert secrets
vault kv put -mount connections Secrets jwtSecret=hsduehjrebxbbjklwxp39948788akkkkedlpahheb156512989736363yggs jwtIssuer=AuthService

# Loop forever to prevent container from terminating

while :
do
	sleep 3600
done