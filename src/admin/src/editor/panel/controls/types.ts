import type { Json, JsonObject } from '../../schema/subsetValidator'

/** What the panel gives every control. */
export interface ControlProps {
  /** The property name, for data-prop and ids. */
  prop: string
  /** A unique id for the control element. */
  id: string
  label: string
  /** The property's schema with any $ref followed. */
  schema: JsonObject
  /** The stored value; undefined when the property is missing. */
  value: Json | undefined
  /** The schema default, shown when the value is missing. */
  defaultValue?: Json
  required?: boolean
  /** Why the last change was refused, if it was. */
  error?: string
  siteId: string
}

export const describedBy = (id: string, error?: string, help?: string): string | undefined =>
  [help ? `${id}-help` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined
