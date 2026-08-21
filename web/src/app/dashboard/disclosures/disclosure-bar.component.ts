import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { DashboardStore } from '../dashboard-store';

@Component({
  selector: 'app-disclosure-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (response(); as response) {
      <div class="disclosures">
        <p class="disclosures__tz">{{ response.timezoneNote }}</p>

        @if (response.disclosures.nullOutcomeCount > 0) {
          <p class="disclosures__note">
            {{ response.disclosures.nullOutcomeCount }} event(s) have no recorded outcome and are excluded from
            rate calculations, but still counted in the event-type total.
          </p>
        }

        @for (exclusion of response.disclosures.exclusions; track exclusion.fromDate + exclusion.reason) {
          <p class="disclosures__exclusion">
            Data quality exclusion {{ exclusion.fromDate }}
            @if (exclusion.toDate !== exclusion.fromDate) {
              – {{ exclusion.toDate }}
            }
            : {{ exclusion.reason }}
          </p>
        }
      </div>
    }
  `,
  styles: `
    .disclosures {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      margin-bottom: 1.25rem;
      font-size: 0.8rem;
      color: #595959;
    }
    .disclosures__tz {
      font-style: italic;
    }
    .disclosures__exclusion {
      color: #613400;
    }
  `,
})
export class DisclosureBarComponent {
  private readonly store = inject(DashboardStore);
  readonly response = computed(() => this.store.response());
}
