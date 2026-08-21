import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import { DashboardApiService } from '../core/api/dashboard-api.service';
import { ApiError, DashboardResponseDto } from '../core/api/models/dashboard.model';
import { MetaResponseDto } from '../core/api/models/meta.model';

const DEFAULT_ACCOUNT_ID = 6;

/** 'all' = no `locations` param (server includes every location). An array — including an
 * empty one — is an explicit selection: `[]` means the user unchecked every box, which the
 * API has no way to express (an empty `locations` param means "all"), so it never reaches
 * the API at all — see `noLocationsSelected` below. */
export type LocationSelection = 'all' | string[];

export interface DashboardFilters {
  accountId: number;
  locations: LocationSelection;
  week: string | null;
  window: number | null;
  tolerance: number | null;
}

function filtersEqual(a: DashboardFilters, b: DashboardFilters): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

function toApiError(err: unknown): ApiError {
  const httpErr = err as { status?: number; error?: { title?: string; detail?: string; parameter?: string; message?: string } };
  const body = httpErr?.error;
  return {
    status: httpErr?.status ?? 0,
    title: body?.title ?? 'Request failed',
    detail: body?.detail ?? body?.message ?? 'Something went wrong loading the dashboard.',
    parameter: body?.parameter,
  };
}

/**
 * Signal-based store: the route's query params are the single source of truth. Every filter
 * change is a `router.navigate` with `queryParamsHandling: 'merge'` and `replaceUrl: true`, so
 * the URL is always what a copy-pasted link would reproduce, and the tolerance slider does not
 * spam browser history with one entry per pixel.
 */
@Injectable()
export class DashboardStore {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(DashboardApiService);

  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  readonly filters: Signal<DashboardFilters> = computed(() => {
    const qp = this.queryParams();
    const rawLocations = qp.get('locations');
    const rawWindow = qp.get('window');
    const rawTolerance = qp.get('tolerance');

    return {
      accountId: Number(qp.get('account')) || DEFAULT_ACCOUNT_ID,
      locations: rawLocations === null ? 'all' : rawLocations === '' ? [] : rawLocations.split(','),
      week: qp.get('week'),
      window: rawWindow ? Number(rawWindow) : null,
      tolerance: rawTolerance ? Number(rawTolerance) : null,
    };
  });

  readonly noLocationsSelected = computed(() => {
    const { locations } = this.filters();
    return Array.isArray(locations) && locations.length === 0;
  });

  readonly loading = signal(false);
  readonly error = signal<ApiError | null>(null);

  // /meta is keyed on account + week only (Open Question 3): maxWindowForWeek shrinks as the
  // viewed week moves backwards, and that's the one thing the dashboard response itself
  // doesn't carry — everything else the filter bar needs (the location list, the current
  // week's label/hasPrevious/hasNext) comes back on the dashboard response.
  private readonly metaKey = computed(() => ({ accountId: this.filters().accountId, week: this.filters().week }));

  private readonly metaResponse = toSignal(
    toObservable(this.metaKey).pipe(
      distinctUntilChanged((a, b) => a.accountId === b.accountId && a.week === b.week),
      switchMap((key) => this.api.getMeta(key.accountId, key.week ?? undefined).pipe(catchError(() => of(null)))),
    ),
    { initialValue: null },
  );

  readonly meta: Signal<MetaResponseDto | null> = this.metaResponse;
  // maxWindowForWeek is legitimately 0 at an account's earliest week — the control still needs
  // a usable range there, so the floor is applied here rather than by every consumer.
  readonly maxWindowForWeek = computed(() => Math.max(1, this.meta()?.maxWindowForWeek ?? 1));

  private readonly dashboardResponse = toSignal(
    toObservable(this.filters).pipe(
      debounceTime(250),
      distinctUntilChanged(filtersEqual),
      switchMap((filters) => {
        if (Array.isArray(filters.locations) && filters.locations.length === 0) {
          // Nothing to fetch: an explicit "none selected" is a client-only empty state, not a
          // request the API can express (see LocationSelection above).
          this.loading.set(false);
          this.error.set(null);
          return of(null);
        }

        this.loading.set(true);
        return this.api
          .getDashboard({
            accountId: filters.accountId,
            locations: filters.locations === 'all' ? undefined : filters.locations.join(','),
            week: filters.week,
            window: filters.window,
            tolerance: filters.tolerance,
          })
          .pipe(
            map((response) => {
              this.loading.set(false);
              this.error.set(null);
              return response;
            }),
            catchError((err: unknown) => {
              this.loading.set(false);
              this.error.set(toApiError(err));
              return of(null);
            }),
          );
      }),
    ),
    { initialValue: null },
  );

  readonly response: Signal<DashboardResponseDto | null> = this.dashboardResponse;
  readonly sections = computed(() => this.response()?.sections ?? []);
  readonly hasNoData = computed(() => {
    const r = this.response();
    return r !== null && r.locations.length === 0 && r.sections.length === 0;
  });

  setAccount(accountId: number): void {
    this.navigate({ account: String(accountId) });
  }

  setLocations(selection: LocationSelection): void {
    this.navigate({ locations: selection === 'all' ? null : selection.length ? selection.join(',') : '' });
  }

  setWeek(isoWeek: string): void {
    this.navigate({ week: isoWeek });
  }

  setWindow(window: number): void {
    this.navigate({ window: String(window) });
  }

  setTolerance(tolerance: number): void {
    this.navigate({ tolerance: String(tolerance) });
  }

  private navigate(partial: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: partial,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
