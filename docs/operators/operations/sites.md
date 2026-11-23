# Sites and host names

How a request finds its site, what visitors see for unknown host names, and disabling sites.

## How a request finds its site

Each public request is matched to a site by the host name the visitor used: the `Host`
header, or `X-Forwarded-Host` when it comes from a proxy named in `Hosting:KnownProxies`
or `Hosting:KnownNetworks` (see [configuration](../reference/configuration.md#hosting-and-reverse-proxies)).
Matching ignores letter case, a trailing dot and the port. Internationalised names are
stored in their ASCII (`xn--…`) form and shown readably in the admin; either spelling can
be typed.

Every host name belongs to at most one site, disabled sites included. A site keeps at least
one host name, and one of them is **primary**: the site's base URL is
`{scheme}://{primary}{Hosting:BasePath}`. The base URL never carries a port, so on a
non-standard public port (typical only of local testing) it will not open as written.
Nothing serves requests from the base URL yet.

Changes take effect at the next request, on every instance: nothing caches host names.

## What visitors see

- **A host name no site holds** and **a disabled site's host name** get the same `404`,
  byte for byte. Disabling a site does not reveal that it exists.
- **A path a site does not have** gets the site's not-found page: a `404` HTML page in the
  site's design and language (012). An address whose page is unpublished or never
  published gets exactly the same page.
- **With `Hosting:AdminHostNames` set**, `/api` and `/admin` on any other host get that same
  not-found page, byte for byte.

Neither response, and no log line, contains the host name that was asked for.

## Disabling a site

Disabling takes a site off the public surface at once and keeps everything else: content,
settings, grants and host names, which stay reserved. Site members keep working on it in
the admin. Enabling brings it back exactly as it was. Sites cannot be deleted yet.

## TLS and DNS

Certificates and DNS are yours: point each host name at your reverse proxy and terminate
TLS there. The app only needs to see the host name the visitor used, through the `Host`
header or a trusted proxy's `X-Forwarded-Host`.

## Audit

Adding or removing a host name, and disabling or enabling a site, are recorded as security
events (see [Accounts and sign-in](identity.md#security-events)) with who did it. They appear within a few seconds, once the outbox
delivers them (`Outbox:PollInterval`).
