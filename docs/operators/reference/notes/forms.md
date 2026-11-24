Every request to `/_forms` counts, valid or not. Over a limit, the visitor sees the form
again with their values and a message to wait; the response says how long. Counts are kept
in the database, so every instance shares them, and are deleted after an hour. IPv6
addresses are grouped by their /64 network, as sign-in throttling groups them.

**Behind a reverse proxy, name it in `Hosting:KnownProxies` or `Hosting:KnownNetworks`.**
Otherwise every visitor arrives from the proxy's address and they all share one limit: a
busy site would refuse genuine enquiries after the first few in a minute.
