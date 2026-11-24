**Trusting nothing is the default, and it matters.** Sites are resolved from the `Host`
header, so a spoofable `X-Forwarded-Host` would let a caller choose which site they are
talking to. If you put a proxy in front, name it in `Hosting:KnownProxies` or
`Hosting:KnownNetworks`.

**Sign-in security depends on this too.** Two identity rules read the client's address and
scheme, and behind a proxy they are only correct when the proxy is named:

- per-client sign-in throttling blocks an address after repeated failures, and every
  request through an unnamed proxy appears to come from the proxy, so one attacker would
  block everyone;
- the HTTPS rule refuses credentials sent over plain HTTP from other machines, and it
  cannot see that the original request was HTTPS unless it trusts `X-Forwarded-Proto`.

**Several proxies.** Name every hop you run (for example a CDN and the proxy behind it).
Forwarded values are unwound hop by hop from the nearest one, and unwinding stops at the
first address that is not named, so an unnamed hop can never choose the host.

**robots.txt under a base path.** Search engines look for `/robots.txt` at the root of a host
name only. With `Hosting:BasePath` set, forward that one address to `{BasePath}/robots.txt`
at the proxy; see [Sub-path installation](../reverse-proxy/index.md#sub-path-installation).

**Keeping the admin off client domains.** For an installation that hosts client sites, set
`Hosting:AdminHostNames` to a name only you use. If you mistype it and lose the admin, remove
the setting (or fix it) and restart: it is the only thing that confines the admin.
