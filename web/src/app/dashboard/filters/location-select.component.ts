import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzSelectModule } from 'ng-zorro-antd/select';

import { DashboardStore } from '../dashboard-store';

@Component({
  selector: 'app-location-select',
  imports: [FormsModule, NzSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="filter">
      <span class="filter__label">Locations</span>
      <nz-select
        nzMode="multiple"
        nzPlaceHolder="Select locations…"
        nzAllowClear
        [ngModel]="selectedNames()"
        (ngModelChange)="onChange($event)"
      >
        @for (location of locations(); track location.id) {
          <nz-option [nzValue]="location.name" [nzLabel]="location.name" />
        }
      </nz-select>
    </label>
  `,
  styles: `
    .filter {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-width: 220px;
    }
    .filter__label {
      font-size: 0.75rem;
      font-weight: 600;
      color: #595959;
    }
  `,
})
export class LocationSelectComponent {
  private readonly store = inject(DashboardStore);

  readonly locations = computed(() => this.store.meta()?.locations ?? []);

  readonly selectedNames = computed(() => {
    const selection = this.store.filters().locations;
    return selection === 'all' ? this.locations().map((l) => l.name) : selection;
  });

  onChange(names: string[]): void {
    const allNames = this.locations().map((l) => l.name);
    const isEverything = names.length === allNames.length && allNames.every((n) => names.includes(n));
    this.store.setLocations(isEverything ? 'all' : names);
  }
}
