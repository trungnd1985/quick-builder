# Reverse proxy and TLS

QuickBuilder serves plain HTTP on `127.0.0.1:8080` and never terminates TLS itself. To put
it on the internet, run a reverse proxy in front of it that:

1. holds the certificates and serves HTTPS for every host name: each site's domains and the
   admin's;
2. passes the host name the visitor used, the scheme (`https`) and the visitor's address;
3. allows request bodies as large as your uploads and site imports.

QuickBuilder must also know which proxy to believe. Worked examples, each tested on every
release:

- [Nginx](nginx.md), with certificates from Let's Encrypt via certbot;
- [Caddy](caddy.md), which obtains and renews certificates on its own;
- [Traefik](traefik.md), configured from Docker labels, with its own ACME client.

All three run as one more service in the same Compose project, next to `app` and `db`, from
a second compose file `compose.proxy.yml`. They use these example host names; replace them
with yours everywhere:

| Host name | What it is |
|---|---|
| `example.com`, `www.example.com` | a site |
| `shop.example.net` | another site, on another domain |
| `admin.example.com` | the admin, kept off the sites' domains |

## DNS and certificates for several domains

Point each host name (an `A`/`AAAA` record, or a `CNAME`) at the server running the proxy.
The proxy needs one certificate covering every name, or one per name; each example shows how
its proxy gets them. Certificates and DNS are yours: QuickBuilder only needs to see the host
name the visitor used. A host name is served by the site that holds it (see [Sites and host
names](../operations/sites.md)); a name no site holds gets a plain `404`.

## Forward the host, scheme and address

The proxy must send, on every request:

| Header | Value | Why |
|---|---|---|
| `Host` (or `X-Forwarded-Host`) | the host name the visitor used | It decides which site answers. |
| `X-Forwarded-Proto` | `https` | Sign-in refuses passwords that did not arrive over HTTPS. |
| `X-Forwarded-For` | the visitor's address | Sign-in throttling and form rate limits count per visitor. |

## Trust the proxy {#trust-the-proxy}

QuickBuilder ignores forwarded headers unless they come from an address you name. Trusting
nothing is the default on purpose: a forwarded host from an untrusted source would let anyone
choose which site they reach. Name your proxy in
[`Hosting:KnownProxies`](../reference/configuration.md#hosting-knownproxies) (its address) or
[`Hosting:KnownNetworks`](../reference/configuration.md#hosting-knownnetworks) (a network in
CIDR form). Name every hop: with a CDN in front of your proxy, name both.

The examples give the proxy a fixed address, `172.30.0.10`, on the Compose network
`172.30.0.0/24`, and name that address. If that network is already in use on your server,
pick another private range and change both places. Changing a project's network needs one
`docker compose down` (volumes are kept) before the next `up`.

With the proxy in front, also set:

- [`Identity:AllowInsecureHttp`](../reference/configuration.md#identity-allowinsecurehttp) to
  `false`: credentials over plain HTTP are refused again;
- [`Hosting:AdminHostNames`](../reference/configuration.md#hosting-adminhostnames) to the admin
  host name, so client domains never show the sign-in page;
- [`Hosting:PublicUrl`](../reference/configuration.md#hosting-publicurl) to the admin's address,
  so recovery links and notification emails are complete.

The examples do all three.

## Request size limits {#request-size-limits}

QuickBuilder accepts uploads up to
[`Media:MaxFileSizeBytes`](../reference/configuration.md#media-maxfilesizebytes) and site
archives up to
[`SiteTransfer:MaxUploadSizeBytes`](../reference/configuration.md#sitetransfer-maxuploadsizebytes).
A proxy with a lower limit refuses larger requests with its own `413` before QuickBuilder
sees them. The examples allow 1 GB; raise it if you import larger site archives. Changing a
collection's fields on a large collection can take about 30 seconds in one request, so give
requests at least 60 seconds before timing out.

## Sub-path installation {#sub-path-installation}

To serve QuickBuilder under a path such as `https://example.com/cms/` instead of a host name
of its own, set [`Hosting:BasePath`](../reference/configuration.md#hosting-basepath) to `/cms`
and forward `/cms/` to it, keeping the path. Search engines read `/robots.txt` only at the root
of a host name, so forward that one address to `/cms/robots.txt` too. With Nginx, as a variant
of the [Nginx example](nginx.md):

<!-- qb:file procedure=proxy-basepath step=1 path=nginx/quickbuilder.conf substitute=test-certificates -->
```nginx
server {
    listen 80;
    server_name example.com;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://$host$request_uri; }
}

server {
    listen 443 ssl;
    http2 on;
    server_name example.com;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;

    client_max_body_size 1g;
    proxy_read_timeout   120s;

    proxy_set_header Host              $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;

    # QuickBuilder under /cms/, with the path kept.
    location /cms/ {
        proxy_pass http://app:8080;
        proxy_request_buffering off;
    }

    # Crawlers look for robots.txt at the root only.
    location = /robots.txt {
        proxy_pass http://app:8080/cms/robots.txt;
    }
}
```

<!-- qb:file procedure=proxy-basepath step=2 path=compose.proxy.yml substitute=test-certificates,ports -->
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
      Hosting__BasePath: "/cms"
      Hosting__KnownProxies__0: "172.30.0.10"
      Identity__AllowInsecureHttp: "false"
      Hosting__PublicUrl: "https://example.com/cms"

networks:
  default:
    ipam:
      config:
        - subnet: 172.30.0.0/24
```

<!-- qb:run procedure=proxy-basepath step=3 -->
```bash
echo "COMPOSE_PATH_SEPARATOR=:" >> .env
echo "COMPOSE_FILE=docker-compose.yml:compose.proxy.yml" >> .env
docker compose down
docker compose up -d --wait
```

The admin is then at `https://example.com/cms/admin`.

## Checks and common mistakes {#checks-and-common-mistakes}

After starting the proxy, check each of these. Each mistake below has a symptom you can see
and a fix.

| Check | Symptom of the mistake | Fix |
|---|---|---|
| `curl -sI https://<site host>/` answers from QuickBuilder (`200`, or the site's not-found page) | A plain `404` for every site | The proxy is not passing the visitor's host name, or does not trust it: pass `Host`, and name the proxy in `Hosting:KnownProxies`. |
| Signing in at `https://<admin host>/admin` works | "Credentials must be sent over HTTPS" | The proxy is not trusted, so `X-Forwarded-Proto` is ignored. Name it. |
| After signing in, the newest sign-in event shows your own address (see the [security checklist](../security-checklist.md#trusted-proxies)) | Every visitor has the proxy's address; one failed sign-in burst blocks everyone, and one busy form blocks all visitors | Name the proxy, and make it send `X-Forwarded-For`. |
| Uploading a large file in the media library works | `413 Request Entity Too Large` in the proxy's own style | Raise the proxy's body size limit (see above). |
| `https://<a site host>/admin` answers like a missing page | The sign-in page shows on client domains | Set `Hosting:AdminHostNames`. |
| `http://<any host>/` redirects to `https://` | Pages load over plain HTTP | Add the HTTP-to-HTTPS redirect. |
