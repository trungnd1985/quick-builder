# Nginx

Nginx in front of QuickBuilder, with certificates from Let's Encrypt obtained by certbot.
Checked with nginx 1.27.3. Read [What a proxy must do](index.md) first; it explains each
setting used here. Replace the example host names with yours everywhere.

## 1. Get the certificate

Point every host name at the server, then obtain one certificate for all of them with
certbot's standalone mode while nothing listens on port 80:

```bash
sudo certbot certonly --standalone \
  -d example.com -d www.example.com -d shop.example.net -d admin.example.com
```

Certbot stores it under `/etc/letsencrypt/live/example.com/`. For renewals, use the webroot
method against the directory Nginx serves below: `sudo certbot renew --webroot -w
/var/www/certbot`, then `docker compose exec nginx nginx -s reload` (a certbot deploy hook can
run it).

## 2. Configure Nginx

Save this in the installation directory as `nginx/quickbuilder.conf`:

<!-- qb:file procedure=nginx step=1 path=nginx/quickbuilder.conf substitute=test-certificates -->
```nginx
# Every host name QuickBuilder serves: the sites' domains and the admin's.
# HTTP only answers certificate renewals and redirects to HTTPS.
server {
    listen 80;
    server_name example.com www.example.com shop.example.net admin.example.com;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://$host$request_uri; }
}

server {
    listen 443 ssl;
    http2 on;
    server_name example.com www.example.com shop.example.net admin.example.com;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;

    # Uploads and site archives (Media:MaxFileSizeBytes, SiteTransfer:MaxUploadSizeBytes).
    client_max_body_size 1g;
    # Changing fields of a large collection runs in one request.
    proxy_read_timeout   120s;

    location / {
        proxy_pass http://app:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        # Stream large uploads to QuickBuilder instead of buffering them on disk first.
        proxy_request_buffering off;
    }
}
```

## 3. Add Nginx to the Compose project

Save this next to `docker-compose.yml` as `compose.proxy.yml`. It adds the `nginx` service
with a fixed address, names that address as the trusted proxy, and turns on the settings a
public installation needs:

<!-- qb:file procedure=nginx step=2 path=compose.proxy.yml substitute=test-certificates,ports -->
```yaml
services:
  nginx:
    image: nginx:1.27.3
    restart: unless-stopped
    depends_on: [app]
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx/quickbuilder.conf:/etc/nginx/conf.d/default.conf:ro
      - /etc/letsencrypt:/etc/letsencrypt:ro
      - /var/www/certbot:/var/www/certbot:ro
    networks:
      default:
        ipv4_address: 172.30.0.10

  app:
    environment:
      Hosting__KnownProxies__0: "172.30.0.10"
      Hosting__AdminHostNames__0: "admin.example.com"
      Hosting__PublicUrl: "https://admin.example.com"
      Identity__AllowInsecureHttp: "false"

networks:
  default:
    ipam:
      config:
        - subnet: 172.30.0.0/24
```

## 4. Start it

Tell Compose to use both files from now on, so every `docker compose` command (backups and
upgrades included) sees the proxy, then recreate the project once for the new network:

<!-- qb:run procedure=nginx step=3 -->
```bash
echo "COMPOSE_PATH_SEPARATOR=:" >> .env
echo "COMPOSE_FILE=docker-compose.yml:compose.proxy.yml" >> .env
docker compose down
docker compose up -d --wait
```

Then open `https://admin.example.com/admin`, and work through [Checks and common
mistakes](index.md#checks-and-common-mistakes) and the [security
checklist](../security-checklist.md).
