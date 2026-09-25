import { describe, expect, it, beforeEach, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import SitesView from '../src/features/sites/SitesView.vue'
import { useAuthStore } from '../src/app/authStore'
import en from '../src/i18n/en.json'

// SC-013 and FR-045b. The lint rule (no-raw-text) covers "no strings baked into screens";
// these cover the other half — that the screen is actually usable, and that its three
// states are distinct.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function renderSitesView() {
  return render(SitesView, { global: { plugins: [i18n] } })
}

/**
 * Runs axe against the rendered DOM and returns violations of the recorded baseline.
 *
 * Scoped to the rules in docs/admin-accessibility-baseline.md rather than running
 * everything axe knows: a baseline that is actually enforced beats an aspirational one,
 * and an unscoped run would fail on the missing page-level landmarks that App.vue — not
 * this component — provides.
 */
async function baselineViolations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: {
      type: 'rule',
      values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'],
    },
  })
  return results.violations
}

describe('SitesView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    // Creating sites is for administrators (002 FR-027); these tests exercise that form.
    useAuthStore().signedIn({ id: 'a', displayName: 'Admin', email: 'admin@example.test', isAdministrator: true, siteRoles: [] })
  })

  it('tells a user with no role anywhere that they have no access yet (002)', async () => {
    useAuthStore().signedIn({ id: 'e', displayName: 'Eve', email: 'eve@example.test', isAdministrator: false, siteRoles: [] })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] }))

    renderSitesView()

    await waitFor(() => expect(screen.getByText(en.sites.noAccess.title)).toBeTruthy())
    expect(screen.queryByText(en.sites.empty.title)).toBeNull()
    expect(screen.queryByRole('button', { name: en.sites.create.title })).toBeNull()
  })

  it('shows the empty state when the installation has no sites (FR-014a, FR-045)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] }),
    )

    renderSitesView()

    await waitFor(() => {
      expect(screen.getByText(en.sites.empty.title)).toBeTruthy()
    })

    // The empty state is not an error state. Conflating them is the failure this guards.
    expect(screen.queryByText(en.sites.error.title)).toBeNull()
  })

  it('shows an error state, distinct from empty, when the call fails (FR-045)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        json: async () => ({ title: 'Boom', status: 500, errorCode: 'internal.unexpected' }),
      }),
    )

    renderSitesView()

    await waitFor(() => {
      expect(screen.getByText(en.sites.error.title)).toBeTruthy()
    })

    expect(screen.queryByText(en.sites.empty.title)).toBeNull()
  })

  it('lists sites once loaded', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => [
          {
            id: '0195f3c2-8a1e-7c3d-9b44-2f1a6d5e8c90',
            name: 'Example',
            hostNames: ['example.test'],
            createdAtUtc: '2026-09-22T10:31:04.221Z',
          },
        ],
      }),
    )

    renderSitesView()

    await waitFor(() => {
      expect(screen.getByText('Example')).toBeTruthy()
    })
    expect(screen.getByText('example.test')).toBeTruthy()
  })

  it('marks disabled and HTTP sites, and shows host names readably (009 US4, S-08)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => [
          {
            id: '0195f3c2-8a1e-7c3d-9b44-2f1a6d5e8c91',
            name: 'Offline',
            hostNames: ['xn--bcher-kva.test'],
            hostNameDetails: [{ hostName: 'xn--bcher-kva.test', displayName: 'bücher.test', isPrimary: true }],
            createdAtUtc: '2026-09-22T10:31:04.221Z',
            status: 'disabled',
            scheme: 'http',
          },
        ],
      }),
    )

    renderSitesView()

    await waitFor(() => expect(screen.getByText('Offline')).toBeTruthy())
    expect(screen.getByText(en.sites.status.disabled)).toBeTruthy()
    expect(screen.getByLabelText(en.sites.httpBadgeLabel)).toBeTruthy()
    expect(screen.getByText('bücher.test')).toBeTruthy()
  })

  it('offers scheme and language when creating a site (009 US1)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] }))

    renderSitesView()
    await waitFor(() => expect(screen.getByText(en.sites.empty.title)).toBeTruthy())
    screen.getByRole('button', { name: en.sites.create.title }).click()

    await waitFor(() => expect(screen.getByLabelText(en.sites.create.scheme)).toBeTruthy())
    expect(screen.getByLabelText(en.sites.create.language)).toBeTruthy()
  })

  it('meets the accessibility baseline with the create form open (FR-045b)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] }),
    )

    const { container } = renderSitesView()

    await waitFor(() => {
      expect(screen.getByText(en.sites.empty.title)).toBeTruthy()
    })

    // Open the form so its labelled controls are in the DOM — an empty screen would pass
    // the label rule vacuously.
    screen.getByRole('button', { name: en.sites.create.title }).click()

    await waitFor(() => {
      expect(screen.getByLabelText(en.sites.create.name)).toBeTruthy()
    })

    const violations = await baselineViolations(container)

    expect(
      violations.map((v) => `${v.id}: ${v.description}`),
      'accessibility baseline violations',
    ).toEqual([])
  })

  it('keeps every interactive element keyboard reachable (FR-045b, A1)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] }),
    )

    const { container } = renderSitesView()

    await waitFor(() => {
      expect(screen.getByText(en.sites.empty.title)).toBeTruthy()
    })

    // Nothing may be removed from the tab order. A negative tabindex on a control is the
    // usual way keyboard access quietly breaks.
    const removedFromTabOrder = Array.from(
      container.querySelectorAll('button, input, a[href], select, textarea'),
    ).filter((el) => el.getAttribute('tabindex') === '-1')

    expect(removedFromTabOrder).toEqual([])
  })
})
