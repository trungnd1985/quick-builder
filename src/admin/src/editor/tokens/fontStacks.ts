// The fonts a token set may name: system font stacks from the Modern Font Stacks project
// (CC0), so the editor never downloads a font (011 research ED-17, FR-035, SC-008).
//
// A copy of docs/contracts/design-tokens.md §3 and QuickBuilder.Editor.Contracts.FontStacks.
// DesignTokenContractTests reads this file and fails the build if the lists differ, so keep
// one entry per line in the form below.

export const FONT_STACKS: readonly (readonly [string, string])[] = [
  ['system-ui', "system-ui, sans-serif"],
  ['transitional', "Charter, 'Bitstream Charter', 'Sitka Text', Cambria, serif"],
  ['old-style', "'Iowan Old Style', 'Palatino Linotype', 'URW Palladio L', P052, serif"],
  ['humanist', "Seravek, 'Gill Sans Nova', Ubuntu, Calibri, 'DejaVu Sans', source-sans-pro, sans-serif"],
  ['geometric-humanist', "Avenir, Montserrat, Corbel, 'URW Gothic', source-sans-pro, sans-serif"],
  ['classical-humanist', "Optima, Candara, 'Noto Sans', source-sans-pro, sans-serif"],
  ['neo-grotesque', "Inter, Roboto, 'Helvetica Neue', 'Arial Nova', 'Nimbus Sans', Arial, sans-serif"],
  ['monospace-slab', "'Nimbus Mono PS', 'Courier New', monospace"],
  ['monospace-code', "ui-monospace, 'Cascadia Code', 'Source Code Pro', Menlo, Consolas, 'DejaVu Sans Mono', monospace"],
  ['industrial', "Bahnschrift, 'DIN Alternate', 'Franklin Gothic Medium', 'Nimbus Sans Narrow', sans-serif-condensed, sans-serif"],
  ['rounded-sans', "ui-rounded, 'Hiragino Maru Gothic ProN', Quicksand, Comfortaa, Manjari, 'Arial Rounded MT', 'Arial Rounded MT Bold', Calibri, source-sans-pro, sans-serif"],
  ['slab-serif', "Rockwell, 'Rockwell Nova', 'Roboto Slab', 'DejaVu Serif', 'Sitka Small', serif"],
  ['antique', "Superclarendon, 'Bookman Old Style', 'URW Bookman', 'URW Bookman L', 'Georgia Pro', Georgia, serif"],
  ['didone', "Didot, 'Bodoni MT', 'Noto Serif Display', 'URW Palladio L', P052, Sylfaen, serif"],
  ['handwritten', "'Segoe Print', 'Bradley Hand', Chilanka, TSCu_Comic, casual, cursive"],
]

const FAMILIES: ReadonlyMap<string, string> = new Map(FONT_STACKS)

/** The CSS font-family for a stack id; the system stack for an unknown one. */
export const fontFamily = (id: string): string => FAMILIES.get(id) ?? FAMILIES.get('system-ui')!

export const fontStackIds: readonly string[] = FONT_STACKS.map(([id]) => id)
