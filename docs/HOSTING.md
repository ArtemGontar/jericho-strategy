# Hosting JERICHO: frontend on Vercel, backend on AWS

The game has two parts. The **frontend** is static files (Vercel). The **backend** is one Node process that keeps every online room in memory and talks to browsers over WebSocket (AWS). Solo and hot-seat need only the frontend.

Things that decide the design:
- **Run exactly one backend instance.** Rooms live in that process's memory (and are snapshotted to `DATA_DIR`). Two instances behind a load balancer would split friends into different worlds. Do not use autoscaling.
- **The backend must speak `wss://`** (TLS). A page served over https cannot open a plain `ws://` socket. So the backend needs a domain name and a certificate.
- **Vercel cannot proxy WebSockets**, so the browser connects straight to the AWS backend. The frontend learns the address at build time from `VITE_SERVER_URL`.
- **AWS App Runner does not support WebSockets.** Do not use it.

## What you need before starting
1. A domain you control (for example `example.com`), with DNS you can edit. Two names are used: `play.example.com` (Vercel) and `api.example.com` (AWS).
2. An AWS account and the AWS CLI logged in, region of your choice (pick one near your friends).
3. A Vercel account connected to the GitHub repository `artemgontar/jericho-strategy`, branch `claude/determined-goodall-9vhnhg` (or `main` once merged).

## Step 1 — backend on AWS (one EC2 machine, Docker, Caddy for HTTPS)
Cost is roughly 12 USD a month for a `t4g.small`.

1. Launch an EC2 instance: Amazon Linux 2023 or Ubuntu 24.04, type `t4g.small` (ARM; the Dockerfile builds on ARM and x86), 20 GB disk. Give it an **Elastic IP** so the address never changes.
2. Security group: allow inbound TCP 80 and 443 from anywhere, and 22 only from your own IP. Do not open 8080.
3. Install Docker and the compose plugin, clone the repo, and from the `deploy/` folder run:
   ```
   API_HOST=api.example.com FRONTEND_ORIGIN=https://play.example.com docker compose up -d --build
   ```
4. In your DNS, create an **A record** `api.example.com` pointing to the Elastic IP. Caddy gets the certificate on its own once the name resolves (can take a minute).
5. Check: `curl https://api.example.com/healthz` returns OK, and the container keeps running (`docker compose ps`).

`FRONTEND_ORIGIN` is the exact address of the Vercel site, with `https://` and no trailing slash. The server refuses browser sockets from any other website. If you also want to test from a Vercel preview URL or `localhost`, list several separated by commas (`ALLOWED_ORIGINS`).

No domain? Alternative: put a CloudFront distribution in front of the instance (origin: the instance on port 8080, WebSocket allowed, forward all headers except Host, no caching). CloudFront gives a free `https://xxxx.cloudfront.net` address and the socket URL becomes `wss://xxxx.cloudfront.net/ws`. Then Caddy is not needed (run the `game` service only, with port 8080 open to CloudFront only).

## Step 2 — frontend on Vercel
1. Import the repository in Vercel. `vercel.json` already sets: install `npm ci`, build `npm run build`, output `dist`.
2. Add an environment variable (Production, and Preview if you want previews to work):
   `VITE_SERVER_URL` = `wss://api.example.com/ws`
3. Deploy, then add the domain `play.example.com` to the project and create the DNS record Vercel shows.
4. Open `https://play.example.com`: **Play online → Create a room** should show a five-letter code. Send the invite link to a friend.

`VITE_SERVER_URL` is read when the site is built, so changing it needs a new deployment.

## Step 3 — check it works
- Two different browsers (or one normal and one private window): create a room in one, join with the code in the other, start a game, found a capital in both.
- Close one tab mid-game and reopen the link: the player should return to the same game.
- `docker compose logs -f game` on the machine shows errors; the server logs and keeps running on stray exceptions.

## Keeping it running
- **Update:** `git pull && docker compose up -d --build` in `deploy/`. Running games survive a restart (snapshots in the `jericho-data` volume), but players reconnect.
- **Back up** is not important (rooms are disposable); the volume is only for restarts.
- **Limits** (defaults in `src/server/server.ts`): 16 connections per address, 8 rooms created per address per hour, 12 wrong codes per ten minutes, lobbies expire after 20 minutes, started games after 6 idle hours. Friends on the same home network share one address, so 16 connections is plenty.
- **Memory:** about 0.4 MB per started large-map room; a `t4g.small` (2 GB) is far more than a friend group needs. A `t4g.micro` also works.

## Troubleshooting
| Symptom | Cause |
|---|---|
| "Could not reach the server" on Play online | `VITE_SERVER_URL` missing or wrong at build time, DNS not pointing at the instance yet, or port 443 closed in the security group |
| Socket opens then closes immediately | `FRONTEND_ORIGIN` does not exactly match the address in the browser bar (check `https`, `www`, trailing slash) |
| Mixed content error in the console | `VITE_SERVER_URL` starts with `ws://`; it must be `wss://` |
| Everyone gets "Too many connections" | `TRUST_PROXY=1` missing, so all players look like the proxy's address (it is set in `docker-compose.yml`) |
| Friends cannot join each other's rooms | more than one backend instance is running |
