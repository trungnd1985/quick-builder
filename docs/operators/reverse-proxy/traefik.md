# Traefik

Traefik in front of QuickBuilder, configured from Docker labels, with certificates from Let's
Encrypt obtained by Traefik's own ACME client. Checked with Traefik 3.6.2. Read [What a proxy
must do](index.md) first; replace the example host names with yours everywhere.

## 1. Add Traefik to the Compose project

Save this next to `docker-compose.yml` as `compose.proxy.yml`. Traefik reads its routes from
the labels on the `app` service; the email address is where Let's Encrypt sends expiry
warnings:

<!-- qb:file procedure=traefik step=1 path=compose.proxy.yml substitute=test-certificates,ports -->
```yaml
services:
  traefik:
    image: traefik:3.6.2
    restart: unless-stopped
    command:
      - --providers.docker=true
      - --providers.docker.exposedbydefault=false
      - --entrypoints.web.address=:80
      - --entrypoints.web.http.redirections.entrypoint.to=websecure
      - --entrypoints.web.http.redirections.entrypoint.scheme=https
      - --entrypoints.websecure.address=:443
      - --entrypoints.websecure.transport.respondingTimeouts.readTimeout=120s
      - --certificatesresolvers.le.acme.email=you@example.com
      - --certificatesresolvers.le.acme.storage=/letsencrypt/acme.json
      - --certificatesresolvers.le.acme.httpchallenge.entrypoint=web
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - traefik-acme:/letsencrypt
    networks:
      default:
        ipv4_address: 172.30.0.10

  app:
    labels:
      - traefik.enable=true
      - traefik.http.routers.quickbuilder.rule=Host(`example.com`) || Host(`www.example.com`) || Host(`shop.example.net`) || Host(`admin.example.com`)
      - traefik.http.routers.quickbuilder.entrypoints=websecure
      - traefik.http.routers.quickbuilder.tls.certresolver=le
      - traefik.http.services.quickbuilder.loadbalancer.server.port=8080
    environment:
      Hosting__KnownProxies__0: "172.30.0.10"
      Hosting__AdminHostNames__0: "admin.example.com"
      Hosting__PublicUrl: "https://admin.example.com"
      Identity__AllowInsecureHttp: "false"

volumes:
  traefik-acme:

networks:
  default:
    ipam:
      config:
        - subnet: 172.30.0.0/24
```

What Traefik does by default, and why it is right here:

- It passes the visitor's `Host`, and sets `X-Forwarded-Proto` and `X-Forwarded-For`.
  It does not trust forwarded headers sent by visitors themselves (no
  `forwardedHeaders.trustedIPs` on the entry points), so nobody can pretend to be another
  address. With a CDN or load balancer in front of Traefik, add its addresses as
  `--entrypoints.websecure.forwardedHeaders.trustedIPs=…`.
- It does not limit request bodies, so uploads and site archives are limited only by
  QuickBuilder's own settings. Traefik's `buffering` middleware can set a limit, but it holds
  each request in memory or on disk first, which large uploads should avoid.

Traefik releases before 3.6.1 cannot talk to Docker Engine 29 or newer (their Docker client
asks for an API version the engine no longer offers, and the log repeats "Error response from
daemon"); use the version above or newer.

Giving Traefik the Docker socket lets it read every container's labels; mount it read-only,
as above, and run nothing else on that host that you would not let Traefik see.

## 2. Start it

Tell Compose to use both files from now on, then recreate the project once for the new
network:

<!-- qb:run procedure=traefik step=2 -->
```bash
echo "COMPOSE_PATH_SEPARATOR=:" >> .env
echo "COMPOSE_FILE=docker-compose.yml:compose.proxy.yml" >> .env
docker compose down
docker compose up -d --wait
```

`docker compose logs traefik` shows the certificates being obtained. Then open
`https://admin.example.com/admin`, and work through [Checks and common
mistakes](index.md#checks-and-common-mistakes) and the [security
checklist](../security-checklist.md).
