The admin requires sign-in. A fresh installation is claimed through the setup wizard with a
one-time code printed in the logs, and until setup is finished whoever has that code can
become the administrator. The reference `docker-compose.yml` therefore publishes the app on
`127.0.0.1`. Finish setup first; if you then widen the binding, follow the
[security checklist](../security-checklist.md).
