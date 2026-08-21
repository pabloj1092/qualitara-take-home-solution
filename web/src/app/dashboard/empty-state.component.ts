import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NzEmptyModule } from 'ng-zorro-antd/empty';

export type EmptyStateReason = 'no-data' | 'no-selection';

@Component({
  selector: 'app-empty-state',
  imports: [NzEmptyModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-empty [nzNotFoundContent]="description()" />
  `,
})
export class EmptyStateComponent {
  readonly reason = input.required<EmptyStateReason>();
  readonly accountName = input<string | null>(null);

  readonly description = computed(() =>
    this.reason() === 'no-selection'
      ? 'No locations are selected. Pick at least one location to see its dashboard.'
      : `No locations are reporting for ${this.accountName() ?? 'this account'}.`,
  );
}
