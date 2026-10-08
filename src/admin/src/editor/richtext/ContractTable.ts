// Tables, as the contract has them (035 US5, research RT-06): rows of cells holding paragraphs,
// an optional header row, no merged cells and no column widths. Adding rows, columns and
// tables goes through the commands below, which keep within the limits — 200 rows, 20
// columns, 20 tables per rich text (spec edge case "Limits") — and say which one stopped them.

import { findParentNode, type Editor } from '@tiptap/core'
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table'
import type { Node as ProseMirrorNode } from '@tiptap/pm/model'
import { MAX_RICH_TEXT_TABLES } from '../schema/limits'
import type { RichTextNotice } from './paste'

export const MAX_TABLE_ROWS = 200
export const MAX_TABLE_COLUMNS = 20

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    contractTable: {
      /** A 3 × 3 table with a header row, unless the rich text already holds the most tables. */
      insertContractTable: () => ReturnType
      /** A row above or below the cursor's, within the row limit. */
      addContractRow: (where: 'before' | 'after') => ReturnType
      /** A column before or after the cursor's, within the column limit. */
      addContractColumn: (where: 'before' | 'after') => ReturnType
    }
  }
}

export interface ContractTableOptions {
  onNotice?: (notice: RichTextNotice) => void
}

const tableAtCursor = (editor: Editor): ProseMirrorNode | null =>
  findParentNode((node) => node.type.name === 'table')(editor.state.selection)?.node ?? null

const tablesIn = (doc: ProseMirrorNode): number => {
  let count = 0
  doc.descendants((node) => {
    if (node.type.name === 'table') count++
    return node.type.name !== 'table'
  })
  return count
}

export const ContractTable = Table.extend<ContractTableOptions & Record<string, unknown>>({
  addOptions() {
    return { ...this.parent?.(), resizable: false, onNotice: undefined }
  },

  addCommands() {
    const notice = (limit: 'rows' | 'columns' | 'tables'): false => {
      this.options.onNotice?.({ kind: 'limit', limit })
      return false
    }

    return {
      ...this.parent?.(),
      insertContractTable: () => ({ editor, commands }) =>
        tablesIn(editor.state.doc) >= MAX_RICH_TEXT_TABLES
          ? notice('tables')
          : commands.insertTable({ rows: 3, cols: 3, withHeaderRow: true }),
      addContractRow: (where) => ({ editor, commands }) => {
        const table = tableAtCursor(editor)
        if (table && table.childCount >= MAX_TABLE_ROWS) return notice('rows')
        return where === 'before' ? commands.addRowBefore() : commands.addRowAfter()
      },
      addContractColumn: (where) => ({ editor, commands }) => {
        const table = tableAtCursor(editor)
        if (table && (table.firstChild?.childCount ?? 0) >= MAX_TABLE_COLUMNS) return notice('columns')
        return where === 'before' ? commands.addColumnBefore() : commands.addColumnAfter()
      },
    }
  },

  addKeyboardShortcuts() {
    return {
      ...this.parent?.(),
      // Tab in the last cell adds a row (FR-011), within the row limit.
      Tab: () => {
        if (this.editor.commands.goToNextCell()) return true
        if (!tableAtCursor(this.editor)) return false
        if (!this.editor.commands.addContractRow('after')) return true
        return this.editor.commands.goToNextCell()
      },
    }
  },
})

/** Cells hold paragraphs only, and never span (contract `rtTableCell`). */
export const ContractTableCell = TableCell.extend({ content: 'paragraph+' })
export const ContractTableHeader = TableHeader.extend({ content: 'paragraph+' })
export { TableRow as ContractTableRow }
