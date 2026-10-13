// Every change the editor makes to a document is one of these pure functions (011 research
// ED-10). Components never change a document themselves: they call an operation through the
// editor store, which records it for undo and schedules the autosave.

export { insert } from './insert'
export { move } from './move'
export { remove } from './remove'
export { duplicate } from './duplicate'
export { paste } from './paste'
export { setLabel, setProp } from './setProp'
export { setSeo } from './setSeo'
export type { OpRefusal, OpRefusalCode, OpResult } from './types'
