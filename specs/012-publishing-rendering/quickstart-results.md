# Quickstart results: Publishing and rendering (#12)

## Public texts awaiting native review

The not-found page's three strings (`notFound.title`, `notFound.message`, `notFound.home`)
exist for all 40 supported languages in
`src/Modules/Rendering/QuickBuilder.Rendering/Resources/public-texts/` (FR-030, research
PR-13). English is the source. The other 39 were drafted without a native speaker. Each one
needs a native review before it is called final:

`ar`, `bg`, `cs`, `da`, `de`, `el`, `es`, `et`, `fa`, `fi`, `fr`, `he`, `hi`, `hr`, `hu`,
`id`, `it`, `ja`, `ko`, `lt`, `lv`, `ms`, `nb`, `nl`, `pl`, `pt`, `pt-BR`, `ro`, `ru`, `sk`,
`sl`, `sr`, `sv`, `th`, `tr`, `uk`, `vi`, `zh-Hans`, `zh-Hant`.

A reviewer changes only the three values, keeps the keys, and runs
`PublicTextCatalogueTests`.
