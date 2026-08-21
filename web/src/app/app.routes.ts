import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';

import { DashboardPageComponent } from './dashboard/dashboard-page.component';

// Requirements: "Use Router query params to select the account and locations" — account is a
// query param on a single route, not a path segment. No account switcher exists in the UI (it
// isn't in the spec's component tree), so a fresh visit is redirected once to a concrete
// account rather than rendering against an undefined one; from there the URL is what a
// support link would carry.
const DEFAULT_ACCOUNT_ID = '6';

const ensureAccountQueryParam: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const url = router.parseUrl(state.url);
  if (url.queryParams['account']) {
    return true;
  }

  return router.createUrlTree([], {
    queryParams: { ...url.queryParams, account: DEFAULT_ACCOUNT_ID },
    queryParamsHandling: 'merge',
  });
};

export const routes: Routes = [
  { path: '', component: DashboardPageComponent, canActivate: [ensureAccountQueryParam] },
];
