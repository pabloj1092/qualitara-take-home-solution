import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DashboardResponseDto } from './models/dashboard.model';
import { MetaResponseDto } from './models/meta.model';

// Program.cs's "AngularDev" CORS policy is scoped to http://localhost:4200 specifically to let
// the dev server call this origin directly — see docs/AI_LOG or Program.cs for the rationale.
const API_BASE_URL = 'http://localhost:5080/api';

export interface GetDashboardParams {
  accountId: number;
  locations?: string;
  week?: string | null;
  window?: number | null;
  tolerance?: number | null;
}

@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private readonly http = inject(HttpClient);

  getDashboard(params: GetDashboardParams): Observable<DashboardResponseDto> {
    let httpParams = new HttpParams();
    if (params.locations) {
      httpParams = httpParams.set('locations', params.locations);
    }
    if (params.week) {
      httpParams = httpParams.set('week', params.week);
    }
    if (params.window != null) {
      httpParams = httpParams.set('window', params.window);
    }
    if (params.tolerance != null) {
      httpParams = httpParams.set('tolerance', params.tolerance);
    }

    return this.http.get<DashboardResponseDto>(
      `${API_BASE_URL}/accounts/${params.accountId}/dashboard`,
      { params: httpParams },
    );
  }

  getMeta(accountId: number, week?: string | null): Observable<MetaResponseDto> {
    let httpParams = new HttpParams();
    if (week) {
      httpParams = httpParams.set('week', week);
    }

    return this.http.get<MetaResponseDto>(
      `${API_BASE_URL}/accounts/${accountId}/meta`,
      { params: httpParams },
    );
  }
}
