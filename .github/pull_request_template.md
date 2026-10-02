## What and why

## How it was tested
- [ ] `dotnet test --filter "Category!=Integration"`
- [ ] Deployed to the dev stack (`dev/deploy-plugin.sh`) if behaviour changed

## Checklist
- [ ] Conventional Commit title
- [ ] No secrets in logs (URLs go through `SecretMasker`)
- [ ] Jellyfin-internal code stays in `Integration/`
- [ ] ADR added/updated for architectural changes
