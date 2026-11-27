import { describe, expect, it } from 'vitest';
import { formKey, shouldInitForm } from './formInit';

// I2 (0.29.2 final review): the language switch refetches every query, and a record comes back as a new object (its picked names
// change). The full-page edit forms must not re-initialise for that, or they throw away what the person typed.
describe('edit form set-up (0.29.2)', () => {
  const wo = { id: 'w1', rowVersion: 'AAA=', assetName: 'Pump', location: ['Plant', 'Room 1'] };

  it('does not re-initialise for a refetch of the same record version (a language switch)', () => {
    const arabic = { ...wo, assetName: 'مضخة', location: ['المصنع', 'الغرفة 1'] };
    expect(arabic).not.toBe(wo);
    expect(shouldInitForm(wo, arabic)).toBe(false);
    expect(formKey(wo, undefined)).toBe(formKey(arabic, undefined));
  });

  it('re-initialises on first load, for another record, and for a newer version', () => {
    expect(shouldInitForm(undefined, wo)).toBe(true);
    expect(shouldInitForm(wo, { ...wo, id: 'w2' })).toBe(true);
    expect(shouldInitForm(wo, { ...wo, rowVersion: 'AAB=' })).toBe(true);
    expect(shouldInitForm(wo, undefined)).toBe(true);
  });

  it('uses the change time when a record has no row version', () => {
    const a = { id: 'x', updatedAt: '2026-11-27T10:00:00Z' };
    expect(shouldInitForm(a, { ...a })).toBe(false);
    expect(shouldInitForm(a, { ...a, updatedAt: '2026-11-27T10:05:00Z' })).toBe(true);
  });

  it('keys on every record a form is set up from, in order', () => {
    const asset = { id: 'a1', rowVersion: 'V1' };
    expect(formKey(undefined, asset)).not.toBe(formKey(asset, undefined));
    expect(formKey(wo, asset)).toBe(formKey({ ...wo }, { ...asset }));
    expect(formKey(wo, asset)).not.toBe(formKey(wo, { ...asset, rowVersion: 'V2' }));
  });
});

/** The full-page edit forms (reachable while the header's language switch is): each keys its set-up on formKey, never on the object. */
const forms = Object.entries(
  import.meta.glob(
    ['../pages/work/WorkOrderFormPage.tsx', '../pages/pm/PmScheduleFormPage.tsx', '../pages/pm/TemplatePage.tsx', '../pages/assets/AssetFormPage.tsx',
      '../pages/inventory/PartFormPage.tsx', '../pages/contracts/ContractFormPage.tsx', '../pages/purchasing/PoFormPage.tsx', '../pages/purchasing/RequestFormPage.tsx'],
    { query: '?raw', import: 'default', eager: true },
  ),
) as [string, string][];

describe('full-page edit forms', () => {
  it('set themselves up from the record identity and version only', () => {
    expect(forms).toHaveLength(8);
    const keyedOnObject = forms.filter(([, text]) => !/\}, \[formKey\(/.test(text)).map(([path]) => path);
    expect(keyedOnObject).toEqual([]);
  });
});
