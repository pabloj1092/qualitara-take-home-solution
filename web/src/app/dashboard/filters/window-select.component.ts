import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';

import { DashboardStore } from '../dashboard-store';

const DEFAULT_WINDOW = 8;

@Component({
  selector: 'app-window-select',
  imports: [FormsModule, NzInputNumberModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="filter">
      <span class="filter__label">Comparison window (weeks)</span>
      <nz-input-number
        [ngModel]="value()"
        (ngModelChange)="onChange($event)"
        [nzMin]="1"
        [nzMax]="max()"
        [nzStep]="1"
        [attr.aria-label]="'Comparison window in weeks, max ' + max()"
      />
    </label>
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
  `,
})
export class WindowSelectComponent {
  private readonly store = inject(DashboardStore);

  readonly max = this.store.maxWindowForWeek;

  readonly value = computed(() => {
    const requested = this.store.filters().window ?? this.store.response()?.window.requested ?? DEFAULT_WINDOW;
    return Math.min(requested, this.max());
  });

  onChange(value: number | null): void {
    if (value === null || Number.isNaN(value)) {
      return;
    }
    this.store.setWindow(value);
  }
}
