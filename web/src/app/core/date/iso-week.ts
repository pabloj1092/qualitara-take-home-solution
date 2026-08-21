// Minimal ISO-8601 week arithmetic for the week-picker's prev/next arrows. Mirrors the same
// rule Relay.Domain.WeekRange uses server-side (System.Globalization.ISOWeek): the Monday of
// week 1 is the Monday of the week containing 4 January. All arithmetic is done in UTC purely
// as calendar math — no timezone is attached to an ISO week string, so there is nothing to
// convert; the server already resolved timezone boundaries before an ISO week ever reaches here.

function isoWeekToMonday(isoWeek: string): Date {
  const match = /^(\d{4})-W(\d{2})$/.exec(isoWeek);
  if (!match) {
    throw new Error(`'${isoWeek}' is not a valid ISO week (expected yyyy-Www).`);
  }

  const year = Number(match[1]);
  const week = Number(match[2]);
  const jan4 = new Date(Date.UTC(year, 0, 4));
  const jan4DayOfWeek = (jan4.getUTCDay() + 6) % 7; // Monday = 0 .. Sunday = 6
  const week1Monday = new Date(jan4);
  week1Monday.setUTCDate(jan4.getUTCDate() - jan4DayOfWeek);

  const monday = new Date(week1Monday);
  monday.setUTCDate(week1Monday.getUTCDate() + (week - 1) * 7);
  return monday;
}

function mondayToIsoWeek(monday: Date): string {
  const thursday = new Date(monday);
  thursday.setUTCDate(monday.getUTCDate() + 3);
  const isoYear = thursday.getUTCFullYear();

  const jan4 = new Date(Date.UTC(isoYear, 0, 4));
  const jan4DayOfWeek = (jan4.getUTCDay() + 6) % 7;
  const week1Monday = new Date(jan4);
  week1Monday.setUTCDate(jan4.getUTCDate() - jan4DayOfWeek);

  const diffDays = Math.round((thursday.getTime() - week1Monday.getTime()) / 86_400_000);
  const week = Math.floor(diffDays / 7) + 1;
  return `${isoYear}-W${String(week).padStart(2, '0')}`;
}

export function nextIsoWeek(isoWeek: string): string {
  const monday = isoWeekToMonday(isoWeek);
  monday.setUTCDate(monday.getUTCDate() + 7);
  return mondayToIsoWeek(monday);
}

export function previousIsoWeek(isoWeek: string): string {
  const monday = isoWeekToMonday(isoWeek);
  monday.setUTCDate(monday.getUTCDate() - 7);
  return mondayToIsoWeek(monday);
}
