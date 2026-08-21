import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { DashboardResponseDto } from '../core/api/models/dashboard.model';
import { MetaResponseDto } from '../core/api/models/meta.model';
import { DashboardStore } from './dashboard-store';

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// Real waits, deliberately longer than DashboardStore's debounceTime(250), rather than fake
// timers: this app is zoneless, and `toObservable`'s first emission is scheduled via an
// Angular `effect()` (a microtask, not a signal-graph-synchronous read), which makes exact
// fake-timer/microtask interleaving brittle without proving anything a real wait doesn't.
const SETTLE_MS = 320;

function metaFixture(overrides: Partial<MetaResponseDto> = {}): MetaResponseDto {
  return {
    locations: [
      { id: 1, name: 'Site A', openedOn: null, closedOn: null },
      { id: 2, name: 'Site B', openedOn: null, closedOn: null },
    ],
    firstWeek: '2026-W05',
    latestWeekWithData: '2026-W31',
    latestCompleteWeek: '2026-W30',
    maxWindowForWeek: 24,
    defaults: {
      week: '2026-W30',
      window: 8,
      tolerancePct: 40,
      minBaselineEvents: 5,
      minRateDenominator: 20,
      minHistoryWeeks: 4,
      minWeekCompleteness: 6 / 7,
      amberFraction: 0.8,
    },
    ...overrides,
  };
}

function dashboardFixture(overrides: Partial<DashboardResponseDto> = {}): DashboardResponseDto {
  return {
    accountId: 6,
    accountName: 'Metro Collision Centers',
    timezone: 'America/New_York',
    timezoneNote: 'All figures in America/New_York (account timezone).',
    week: {
      isoWeek: '2026-W30',
      start: '2026-07-20',
      end: '2026-07-26',
      label: 'Week of Mon 20 Jul – Sun 26 Jul 2026',
      hasPrevious: true,
      hasNext: true,
    },
    window: { requested: 8, effective: 8 },
    tolerancePct: 40,
    locations: [
      { id: 1, name: 'Site A', selected: true },
      { id: 2, name: 'Site B', selected: true },
    ],
    sections: [],
    disclosures: { nullOutcomeCount: 0, exclusions: [] },
    ...overrides,
  };
}

describe('DashboardStore', () => {
  let httpMock: HttpTestingController;
  let queryParams$: BehaviorSubject<ParamMap>;
  let navigateCalls: Array<Record<string, string | null>>;
  let store: DashboardStore;

  function currentParams(): Record<string, string> {
    const map = queryParams$.value;
    return Object.fromEntries(map.keys.map((k) => [k, map.get(k) as string]));
  }

  function setup(initial: Record<string, string> = {}) {
    queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(initial));
    navigateCalls = [];

    const routerStub = {
      navigate: (_commands: unknown[], extras: { queryParams: Record<string, string | null> }) => {
        navigateCalls.push(extras.queryParams);
        const merged = { ...currentParams() };
        for (const [key, value] of Object.entries(extras.queryParams)) {
          if (value === null) {
            delete merged[key];
          } else {
            merged[key] = value;
          }
        }
        queryParams$.next(convertToParamMap(merged));
        return Promise.resolve(true);
      },
    };

    const activatedRouteStub = {
      queryParamMap: queryParams$.asObservable(),
      snapshot: { queryParamMap: queryParams$.value },
    };

    TestBed.configureTestingModule({
      providers: [
        DashboardStore,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: activatedRouteStub },
        { provide: Router, useValue: routerStub },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    store = TestBed.inject(DashboardStore);
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('parses all four filters from the URL on load (the round-trip §6 exists to prove)', async () => {
    setup({
      account: '6',
      locations: 'Site A,Site C',
      week: '2026-W23',
      window: '5',
      tolerance: '30',
    });

    expect(store.filters()).toEqual({
      accountId: 6,
      locations: ['Site A', 'Site C'],
      week: '2026-W23',
      window: 5,
      tolerance: 30,
    });

    await delay(SETTLE_MS);

    httpMock.expectOne((r) => r.url.includes('/meta')).flush(metaFixture());

    const req = httpMock.expectOne((r) => r.url.includes('/dashboard'));
    expect(req.request.params.get('locations')).toBe('Site A,Site C');
    expect(req.request.params.get('week')).toBe('2026-W23');
    expect(req.request.params.get('window')).toBe('5');
    expect(req.request.params.get('tolerance')).toBe('30');
    req.flush(dashboardFixture());
  });

  it('round-trips a filter change back onto the URL via merge + replaceUrl (never a plain history push)', async () => {
    setup({ account: '6' });
    await delay(SETTLE_MS);
    httpMock.expectOne((r) => r.url.includes('/meta')).flush(metaFixture());
    httpMock.expectOne((r) => r.url.includes('/dashboard')).flush(dashboardFixture());

    store.setWindow(12);
    await delay(SETTLE_MS);

    expect(navigateCalls).toContainEqual({ window: '12' });
    expect(store.filters().window).toBe(12);

    const req = httpMock.expectOne((r) => r.url.includes('/dashboard'));
    expect(req.request.params.get('window')).toBe('12');
    req.flush(dashboardFixture({ window: { requested: 12, effective: 12 } }));
  });

  it('treats an explicit empty `locations` param as "nothing selected" and never calls the dashboard endpoint for it', async () => {
    setup({ account: '6', locations: '' });
    await delay(SETTLE_MS);
    httpMock.expectOne((r) => r.url.includes('/meta')).flush(metaFixture());

    expect(store.noLocationsSelected()).toBe(true);
    httpMock.expectNone((r) => r.url.includes('/dashboard'));
  });

  it('cancels the in-flight request when a rapid second filter change lands before the first resolves, and the last response wins (§6 #4)', async () => {
    setup({ account: '6', tolerance: '40' });
    await delay(SETTLE_MS);
    httpMock.expectOne((r) => r.url.includes('/meta')).flush(metaFixture());

    const first = httpMock.expectOne((r) => r.url.includes('/dashboard'));
    expect(first.request.params.get('tolerance')).toBe('40');
    // Deliberately not flushed yet — it must still be in flight when the next change lands.

    store.setTolerance(85);
    await delay(SETTLE_MS);

    expect(first.cancelled).toBe(true);

    const second = httpMock.expectOne((r) => r.url.includes('/dashboard'));
    expect(second.request.params.get('tolerance')).toBe('85');
    second.flush(dashboardFixture({ tolerancePct: 85 }));
    await delay(0);

    expect(store.response()?.tolerancePct).toBe(85);
  });

  it('keys /meta on account + week (not the dashboard filters) so maxWindowForWeek tracks the viewed week', async () => {
    setup({ account: '6', week: '2026-W10' });
    await delay(SETTLE_MS);

    const metaReq = httpMock.expectOne((r) => r.url.includes('/meta'));
    expect(metaReq.request.params.get('week')).toBe('2026-W10');
    metaReq.flush(metaFixture({ maxWindowForWeek: 3 }));
    await delay(0);

    expect(store.maxWindowForWeek()).toBe(3);

    httpMock.expectOne((r) => r.url.includes('/dashboard')).flush(dashboardFixture());
  });

  it('floors maxWindowForWeek at 1 even when the API reports 0 (the earliest week, legitimately)', async () => {
    setup({ account: '6' });
    await delay(SETTLE_MS);
    httpMock.expectOne((r) => r.url.includes('/meta')).flush(metaFixture({ maxWindowForWeek: 0 }));
    await delay(0);

    expect(store.maxWindowForWeek()).toBe(1);

    httpMock.expectOne((r) => r.url.includes('/dashboard')).flush(dashboardFixture());
  });
});
