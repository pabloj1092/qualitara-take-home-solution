import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NzButtonModule } from 'ng-zorro-antd/button';

import { nextIsoWeek, previousIsoWeek } from '../../core/date/iso-week';
import { DashboardStore } from '../dashboard-store';

@Component({
  selector: 'app-week-picker',
  imports: [NzButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filter">
      <span class="filter__label">Viewed week</span>
      <div class="week-picker">
        <button
          nz-button
          nzShape="circle"
          type="button"
          aria-label="Previous week"
          [disabled]="!hasPrevious()"
          (click)="onPrevious()"
        >
          ‹
        </button>
        <span class="week-picker__label">{{ label() }}</span>
        <button
          nz-button
          nzShape="circle"
          type="button"
          aria-label="Next week"
          [disabled]="!hasNext()"
          (click)="onNext()"
        >
          ›
        </button>
      </div>
    </div>
  `,
  styles: `
    .filter {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .filter__label {
      font-size: 0.75rem;
      font-weight: 600;
      color: #595959;
    }
    .week-picker {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .week-picker__label {
      min-width: 15rem;
      text-align: center;
      font-weight: 500;
    }
  `,
})
export class WeekPickerComponent {
  private readonly store = inject(DashboardStore);

  private readonly week = computed(() => this.store.response()?.week ?? null);

  readonly label = computed(() => this.week()?.label ?? 'Loading…');
  readonly hasPrevious = computed(() => this.week()?.hasPrevious ?? false);
  readonly hasNext = computed(() => this.week()?.hasNext ?? false);

  onPrevious(): void {
    const isoWeek = this.week()?.isoWeek;
    if (isoWeek) {
      this.store.setWeek(previousIsoWeek(isoWeek));
    }
  }

  onNext(): void {
    const isoWeek = this.week()?.isoWeek;
    if (isoWeek) {
      this.store.setWeek(nextIsoWeek(isoWeek));
    }
  }
}
