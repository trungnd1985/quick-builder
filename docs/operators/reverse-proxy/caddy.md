# Caddy

Caddy in front of QuickBuilder. Caddy obtains and renews certificates for every host name on
its own, from Let's Encrypt or ZeroSSL, as soon as the names point at the server.
Checked with Caddy 2.9.1. Read [What a proxy must do](index.md) first; replace the example host names with
yours everywhere.

## 1. Configure Caddy

Save this in the installation directory as `caddy/Caddyfile`. The email address is where
the certificate authority sends expiry warnings:

<!-- qb:file procedure=caddy step=1 path=caddy/Caddyfile substitute=test-certificates -->
```text
{
	email you@example.com
}

example.com, www.example.com, shop.example.net, admin.example.com {
	# Uploads and site archives (Media:MaxFileSizeBytes, SiteTransfer:MaxUploadSizeBytes).
	request_body {
		max_size 1GB
	}

	# Caddy passes the visitor's Host and sets X-Forwarded-For and X-Forwarded-Proto.
	reverse_proxy app:8080 {
		transport http {
			read_timeout 120s
		}
	}
}
```

Caddy redirects HTTP to HTTPS by itself.

## 2. Add Caddy to the Compose project

Save this next to `docker-compose.yml` as `compose.proxy.yml`:

<!-- qb:file procedure=caddy step=2 path=compose.proxy.yml substitute=test-certificates,ports -->
```yaml
services:
  caddy:
    image: caddy:2.9.1
    restart: unless-stopped
    depends_on: [app]
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
    volumes:
      - ./caddy/Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
      - caddy-config:/config
    networks:
      default:
        ipv4_address: 172.30.0.10

  app:
    environment:
      Hosting__KnownProxies__0: "172.30.0.10"
      Hosting__AdminHostNames__0: "admin.example.com"
      Hosting__PublicUrl: "https://admin.example.com"
      Identity__AllowInsecureHttp: "false"

volumes:
  caddy-data:
  caddy-config:

networks:
  default:
    ipam:
      config:
        - subnet: 172.30.0.0/24
```

`caddy-data` holds the certificates and the ACME account. Keep it with your backups, or Caddy
requests new certificates after a restore (which is fine within the authority's rate limits).

## 3. Start it

Tell Compose to use both files from now on, then recreate the project once for the new
network:

<!-- qb:run procedure=caddy step=3 -->
```bash
echo "COMPOSE_PATH_SEPARATOR=:" >> .env
echo "COMPOSE_FILE=docker-compose.yml:compose.proxy.yml" >> .env
docker compose down
docker compose up -d --wait
```

`docker compose logs caddy` shows each certificate being obtained. Then open
`https://admin.example.com/admin`, and work through [Checks and common
mistakes](index.md#checks-and-common-mistakes) and the [security
checklist](../security-checklist.md).
