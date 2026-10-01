# Dev stack

Throwaway Jellyfin 12.1 for developing Currents. Never point it at your real library.

1. `cp dev/.env.example dev/.env` and fill in the values.
2. `docker compose -f dev/docker-compose.yml --env-file dev/.env up -d`
3. `dev/bootstrap-jellyfin.sh` completes the setup wizard (admin user `dev` / password `dev`), creates an API key named `currents-dev` and writes it to `dev/.env` as `JELLYFIN_API_KEY`.
4. `dev/make-samples.sh` (once) creates `http://media/sample-720.mp4` and `sample-1080.mp4` for the Jellyfin container.
5. `dev/deploy-plugin.sh` after every change.

Host paths: `dev/data/currents` is `/currents` inside Jellyfin. Set Currents' *Library root* to `/currents/library`.
