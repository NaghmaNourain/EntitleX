import { useCallback, useEffect, useState } from 'react'
import type { NormalizedEntitlement } from '../api/types'

const KEY = 'entitlex.selectedEntitlements'

function read(): NormalizedEntitlement[] {
  try {
    const raw = sessionStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as NormalizedEntitlement[]) : []
  } catch {
    return []
  }
}

export function useSelectionCart() {
  const [items, setItems] = useState<NormalizedEntitlement[]>(() => read())

  useEffect(() => {
    sessionStorage.setItem(KEY, JSON.stringify(items))
  }, [items])

  const toggle = useCallback((entitlement: NormalizedEntitlement) => {
    setItems((prev) => {
      const exists = prev.some(
        (p) =>
          p.entitlementId === entitlement.entitlementId && p.provider === entitlement.provider,
      )
      if (exists) {
        return prev.filter(
          (p) =>
            !(p.entitlementId === entitlement.entitlementId && p.provider === entitlement.provider),
        )
      }
      return [...prev, entitlement]
    })
  }, [])

  const clear = useCallback(() => setItems([]), [])

  const isSelected = useCallback(
    (entitlement: NormalizedEntitlement) =>
      items.some(
        (p) =>
          p.entitlementId === entitlement.entitlementId && p.provider === entitlement.provider,
      ),
    [items],
  )

  return { items, toggle, clear, isSelected, count: items.length }
}
