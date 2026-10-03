# The Currents user page

Each Jellyfin user can choose which AIOStreams config they use and how their streams are ranked. They do this on the
Currents user page.

## Opening the page

- The page is at **`/Currents/user`** on your Jellyfin address, for example `http://192.168.x.y:8096/Currents/user`.
  Include Jellyfin's base URL path if one is set, for example `http://192.168.x.y:8096/jellyfin/Currents/user`.
- The admin page (Dashboard → Plugins → Currents, under **Versions**) shows the exact address.
- The admin must tick **Let users set their own AIOStreams config and preferences** (on by default). The admin can
  also lock one user (**Lock** in the **Users** table). A user who cannot edit sees "Your admin manages these
  settings."
- The user must be signed in to Jellyfin Web in the same browser. The page uses that sign-in.
  - If the browser has no Jellyfin sign-in, the page sends the user to the Jellyfin login.
  - If the session has expired, the next request does the same. Sign in again, then reopen the page.

### A menu entry

Jellyfin has no plugin API for a user-menu entry. To add one, edit jellyfin-web's `config.json` and add:

```json
"menuLinks": [{ "name": "Currents", "icon": "tune", "url": "/Currents/user" }]
```

- If Jellyfin has a base URL path, include it in `url` (for example `"/jellyfin/Currents/user"`).
- In the official Docker image the file is `/jellyfin/jellyfin-web/config.json`.
- Jellyfin overwrites this file on every upgrade. Keep a copy, or mount your own file over it.

## What a user can set

- **AIOStreams manifest URL.** The user's own AIOStreams config, in the form
  `https://<host>/stremio/<uuid>/<password>/manifest.json`.
  - The Jellyfin server checks it with AIOStreams before saving. Saving makes the server contact that address.
  - If AIOStreams refuses it, the page says "AIOStreams did not accept this config. Check the URL and try again."
- **Only show the best stream (hide the version list).** The Version menu then shows only the top-ranked stream.
- **Show titles from AIOMetadata in search, and add them when I open one.** Shown only when the admin has search
  turned on. When it is off, the user sees no AIOMetadata results in search and cannot add titles.
- **Preferences.** Comma-separated lists in order of preference. An empty list means no preference.
  - **Resolutions**, for example `2160p, 1080p`;
  - **HDR / Dolby Vision**: No preference, Prefer HDR, or Avoid HDR (SDR screen);
  - **Audio languages** and **Subtitle languages**;
  - **Largest file (GB, 0 = no limit)**;
  - **Never show these resolutions** and **Never show these tags** (for example `3D`);
  - **Only cached debrid streams**.

Press **Save**. The page says "Saved. Reopen a title to see your versions."

## The saved manifest URL is never shown again

- After saving, the page shows only the host: "Saved: your config on `<host>`."
- Leaving the field empty and pressing **Save** keeps the saved config.
- To replace it, paste the new URL and press **Save**.
- To clear it, press **Use the server's config instead**. The user then gets the config the admin assigned to them,
  or the server's default config.
- **Reset preferences to the server default** clears the user's own preferences, their best-stream choice and their
  search choice. It keeps their AIOStreams config.

## Where each setting comes from

Each setting comes from the first of these that has a value:
1. the user's own setting on this page (when self-service is allowed and the user is not locked);
2. the admin's override for this user (the **Users** table on the admin page);
3. the server default (the admin page);
4. nothing. Titles then show a single version that says the streams are not configured.

Without their own config, a user uses the admin's default AIOStreams config, if the admin set one. The page tells the
user which one is in use:
- "You're using your own AIOStreams config."
- "Your admin assigned an AIOStreams config to you."
- "You're using the server's AIOStreams config."
- "No streams are configured for you yet."

## What the admin can override per user

In the **Users** table on the admin page, per user:
- **Override AIOStreams URL**: a config for this user. The user's own config still wins unless the user is locked.
- **Show only best**: Inherit, Only best, or Show all.
- **Lock**: the user cannot change anything on this page, and their own saved settings are not used.
- **Streams off**: the user gets no streams. The page says "Your admin has turned streams off for your account."
- **Search add off**: the user sees no AIOMetadata search results and cannot add titles. This wins over the user's
  own search switch. The page says "Your admin turned this off for your account."

## Privacy

- Two users see only their own versions. They never see each other's streams or configs.
- Neither the user page nor the admin's **Users** table ever shows a user's saved manifest URL again, only its host.
