namespace QuickBuilder.SmokeTests;

/// <summary>
/// One SQL statement that inserts many collection items as the product stores them (015
/// data-model §1.2): with slugs, status, revision and the derived columns already indexed, so the
/// start-up backfill has nothing to do and timings measure only the operation under test.
/// </summary>
internal static class ItemSeedSql
{
    /// <summary>
    /// Inserts <paramref name="count"/> items, numbered 1…n as <c>g.n</c>, whose values document is
    /// <paramref name="valuesSql"/> (a jsonb expression over <c>g.n</c>), with slug <c>item-n</c>
    /// and name <c>Item n</c>; every <paramref name="publishEvery"/>th is published with a version 1.
    /// </summary>
    public static string Items(Guid siteId, Guid collectionId, int count, string valuesSql, int publishEvery = 0)
    {
        var published = publishEvery > 0 ? $"g.n % {publishEvery} = 0" : "false";
        return
            "INSERT INTO cms.items (id, site_id, collection_id, draft, published, created_at_utc, updated_at_utc, slug, published_slug, status, revision, " +
            "name, search_text, attention, index_version, created_by, updated_by, published_at_utc, published_by, live_version, next_version_number) " +
            "SELECT g.id, s.site, s.coll, v.doc, CASE WHEN p.pub THEN v.doc END, now(), now() - (g.n || ' seconds')::interval, 'item-' || g.n, " +
            "CASE WHEN p.pub THEN 'item-' || g.n END, CASE WHEN p.pub THEN 'published' ELSE 'draft' END, 1, " +
            "'Item ' || g.n, 'item ' || g.n || ' item-' || g.n, NULL, 1, s.nobody, s.nobody, CASE WHEN p.pub THEN now() END, " +
            "CASE WHEN p.pub THEN s.nobody END, CASE WHEN p.pub THEN 1 END, CASE WHEN p.pub THEN 2 ELSE 1 END " +
            $"FROM (SELECT gen_random_uuid() AS id, n FROM generate_series(1, {count}) AS n) g " +
            $"CROSS JOIN (SELECT '{siteId}'::uuid AS site, '{collectionId}'::uuid AS coll, '00000000-0000-0000-0000-000000000000'::uuid AS nobody) s " +
            $"CROSS JOIN LATERAL (SELECT ({valuesSql}) AS doc) v " +
            $"CROSS JOIN LATERAL (SELECT ({published}) AS pub) p; " +
            "INSERT INTO cms.item_slugs (collection_id, slug, item_id, site_id) " +
            $"SELECT collection_id, slug, id, site_id FROM cms.items WHERE collection_id = '{collectionId}'; " +
            "INSERT INTO cms.item_versions (item_id, number, site_id, \\\"values\\\", slug, kind, published_by, published_at_utc) " +
            $"SELECT id, 1, site_id, published, published_slug, 'publish', created_by, now() FROM cms.items WHERE collection_id = '{collectionId}' AND published IS NOT NULL;";
    }
}
