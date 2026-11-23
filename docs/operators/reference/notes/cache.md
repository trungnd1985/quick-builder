The cache never decides what visitors see: each page's cache key includes version numbers
read from the database on every request, so a publish is visible on every instance at its
next request without any shared cache.
