import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzSpinModule } from 'ng-zorro-antd/spin';

import { DisclosureBarComponent } from './disclosures/disclosure-bar.component';
import { EmptyStateComponent } from './empty-state.component';
import { FilterBarComponent } from './filters/filter-bar.component';
import { EventSectionComponent } from './section/event-section.component';
import { DashboardStore } from './dashboard-store';

@Component({
  selector: 'app-dashboard-page',
  providers: [DashboardStore],
  imports: [
    FilterBarComponent,
    DisclosureBarComponent,
    EventSectionComponent,
    EmptyStateComponent,
    NzAlertModule,
    NzSpinModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="dashboard">
      @if (store.response(); as response) {
        <h1 class="dashboard__title">{{ response.accountName }}</h1>
      }

      <app-filter-bar />

      @if (store.error(); as error) {
        <nz-alert nzType="error" [nzMessage]="error.title" [nzDescription]="error.detail" nzShowIcon />
      } @else if (store.noLocationsSelected()) {
        <app-empty-state reason="no-selection" />
      } @else if (store.hasNoData()) {
        <app-empty-state reason="no-data" [accountName]="store.response()?.accountName ?? null" />
      } @else {
        <nz-spin [nzSpinning]="store.loading()">
          <app-disclosure-bar />
          @for (section of store.sections(); track section.eventType) {
            <app-event-section [section]="section" />
          }
        </nz-spin>
      }
    </main>
  `,
  styles: `
    .dashboard {
      max-width: 1100px;
      margin: 0 auto;
      padding: 1.5rem;
    }
    .dashboard__title {
      font-size: 1.4rem;
      margin: 0 0 1rem;
    }
  `,
})
export class DashboardPageComponent {
  protected readonly store = inject(DashboardStore);
}
