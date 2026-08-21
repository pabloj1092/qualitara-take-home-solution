import { TestBed } from '@angular/core/testing';

import { TileStatus } from '../../core/api/models/dashboard.model';
import { StatusBadgeComponent } from './status-badge.component';

// §6 "Each status renders its icon and its text label — assert on the accessible name, which is
// what makes this test worth having rather than a colour-class check." Every assertion here
// reads the element's role/aria-label/text, never a CSS class.
describe('StatusBadgeComponent', () => {
  function render(status: TileStatus) {
    const fixture = TestBed.createComponent(StatusBadgeComponent);
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return el.querySelector('[role="status"]') as HTMLElement;
  }

  it('gives InsufficientData an accessible name of "Not enough data"', () => {
    const badge = render('insufficientData');
    expect(badge.getAttribute('aria-label')).toBe('Not enough data');
    expect(badge.textContent).toContain('Not enough data');
  });

  it('gives PartialWeek an accessible name of "Partial week"', () => {
    const badge = render('partialWeek');
    expect(badge.getAttribute('aria-label')).toBe('Partial week');
  });

  it('gives Breach an accessible name of "Outside tolerance"', () => {
    const badge = render('breach');
    expect(badge.getAttribute('aria-label')).toBe('Outside tolerance');
  });

  it('gives Warning an accessible name of "Near tolerance"', () => {
    const badge = render('warning');
    expect(badge.getAttribute('aria-label')).toBe('Near tolerance');
  });

  it('gives Normal an accessible name of "Normal"', () => {
    const badge = render('normal');
    expect(badge.getAttribute('aria-label')).toBe('Normal');
  });

  it('never renders a bare colour swatch: the icon is always marked decorative and the label always has text', () => {
    const badge = render('breach');
    const icon = badge.querySelector('.status__icon')!;
    const label = badge.querySelector('.status__label')!;
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(label.textContent?.trim().length).toBeGreaterThan(0);
  });

  it('points the Breach arrow down when the deviation is negative', () => {
    const fixture = TestBed.createComponent(StatusBadgeComponent);
    fixture.componentRef.setInput('status', 'breach');
    fixture.componentRef.setInput('deltaPct', -62);
    fixture.detectChanges();
    const icon = (fixture.nativeElement as HTMLElement).querySelector('.status__icon');
    expect(icon?.textContent).toBe('▼');
  });

  it('points the Breach arrow up when the deviation is positive', () => {
    const fixture = TestBed.createComponent(StatusBadgeComponent);
    fixture.componentRef.setInput('status', 'breach');
    fixture.componentRef.setInput('deltaPct', 62);
    fixture.detectChanges();
    const icon = (fixture.nativeElement as HTMLElement).querySelector('.status__icon');
    expect(icon?.textContent).toBe('▲');
  });
});
