#!/bin/sh

# These are set within the script itself, as per your original.
export VAULT_ADDR='http://vaulthost:8200'
export VAULT_TOKEN='00000000-0000-0000-0000-000000000000' # Dev root token

echo "vault-init: Waiting for Vault to start (simple sleep 10s)..."
sleep 10 # Simple wait for dev purposes

echo "vault-init: Vault presumed ready. Writing secrets..."

# --- JWT Configuration (used by AuthService for signing, UserService for validation) ---
# Path: secret/Secrets (as in your original script)
# Keys: Secret, Issuer, Audience (added Audience)
echo "vault-init: Storing JWT parameters at secret/Secrets..."
vault kv put -address="$VAULT_ADDR" secret/Secrets \
    Secret="hsduehjrebxbbjklwxp39948788akkkkedlpahheb156512989736363yggs" \
    Issuer="AuthService" \
    Audience="AuktionshusetAppUsers" # Standard JWT claim, good for validation

# --- Connection Strings and Service URLs ---
# Path: secret/Connections (as in your original script, adding new keys here)
# Keys: mongoConnectionString, MongoDbDatabaseName, AuthServiceUrl, UserServiceUrl
echo "vault-init: Storing MongoDB connection, AuthServiceUrl, and UserServiceUrl at secret/Connections..."
vault kv put -address="$VAULT_ADDR" secret/Connections \
    mongoConnectionString="mongodb://admin:1234@mongodb:27017/" \
    MongoDbDatabaseName="AuktionshusDB" \
    AuthServiceUrl="http://auth_service:8080" \
    UserServiceUrl="http://user_service:8080"
    # Note: Your original also had jwtIssuer here, which is redundant if Issuer is in secret/Secrets

echo "vault-init: Secrets written."
echo "vault-init: Script will now loop indefinitely to keep container running (as per original script)."

# Loop forever to prevent container from terminating (as per your original script)
while :
do
	sleep 3600
done