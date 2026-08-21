import { TestBed } from '@angular/core/testing';

import { SectionDto, TileDto } from '../../core/api/models/dashboard.model';
import { EventSectionComponent } from './event-section.component';

function tile(key: string, label: string): TileDto {
  return {
    key,
    label,
    kind: key.includes('.') ? 'rate' : 'count',
    value: 10,
    deltaPct: null,
    deltaPp: null,
    baselineMean: null,
    bandLow: null,
    bandHigh: null,
    denominator: key.includes('.') ? 20 : null,
    status: 'normal',
    reasonCode: 'withinTolerance',
    baselineWeeksUsed: 8,
    series: [],
  };
}

// Section and outcome order/labels come entirely from the payload's catalogs — never a
// hardcoded 'call_received' in Angular (§ Frontend detail, Component tree).
describe('EventSectionComponent', () => {
  it('renders the section title from displayName, not the raw event-type code', () => {
    const section: SectionDto = {
      eventType: 'lead_created',
      displayName: 'Leads created',
      sortOrder: 2,
      countTile: tile('lead_created', 'Leads created'),
      rateTiles: [],
    };

    const fixture = TestBed.createComponent(EventSectionComponent);
    fixture.componentRef.setInput('section', section);
    fixture.detectChanges();

    const heading = (fixture.nativeElement as HTMLElement).querySelector('h2');
    expect(heading?.textContent).toBe('Leads created');
    expect(heading?.textContent).not.toContain('lead_created');
  });

  it('places the count tile first, then renders rate tiles in the exact order the payload gave them', () => {
    const section: SectionDto = {
      eventType: 'call_received',
      displayName: 'Calls received',
      sortOrder: 1,
      countTile: tile('call_received', 'Calls received'),
      rateTiles: [
        tile('call_received.missed', 'Missed'),
        tile('call_received.voicemail', 'Voicemail'),
        tile('call_received.connected', 'Connected'),
      ],
    };

    const fixture = TestBed.createComponent(EventSectionComponent);
    fixture.componentRef.setInput('section', section);
    fixture.detectChanges();

    const labels = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.tile__label')].map(
      (el) => el.textContent,
    );
    expect(labels).toEqual(['Calls received', 'Missed', 'Voicemail', 'Connected']);
  });
});
