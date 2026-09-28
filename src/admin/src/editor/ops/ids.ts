// Node ids for new blocks (011 research ED-10): 22 base64url characters from a random UUID,
// well inside the contract's ^[A-Za-z0-9_-]{1,64}$ and unique for all practical purposes.

export function newNodeId(): string {
  const hex = crypto.randomUUID().replace(/-/g, '')
  let binary = ''
  for (let i = 0; i < hex.length; i += 2) binary += String.fromCharCode(parseInt(hex.slice(i, i + 2), 16))
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}
