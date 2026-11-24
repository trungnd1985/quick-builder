To apply migrations yourself (with `Database:AutoMigrate` set to `false`), run the `migrate`
command once per upgrade, before starting the application:

```bash
docker compose run --rm app dotnet QuickBuilder.Host.dll migrate
```
