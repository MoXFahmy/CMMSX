import { describe, expect, it } from 'vitest';
import i18next from 'i18next';
import type { TFunction } from 'i18next';
import type { ListLabels } from '@/api/types';
import { applyListLabels, firstVisible, listColor, listOptions } from './listLabels';

const PRIORITIES = ['Low', 'Medium', 'High', 'Urgent'];
const t = ((k: string) => k) as unknown as TFunction;

describe('the organization\'s fixed lists', () => {
  it('orders by the organization\'s order, then the built-in one', () => {
    const labels: ListLabels = { priority: { Urgent: { order: 0 }, Low: { order: 1 } } };
    expect(listOptions('priority', PRIORITIES, labels, t).map((o) => o.value)).toEqual(['Urgent', 'Low', 'Medium', 'High']);
  });
  it('leaves hidden values out, except the record\'s own', () => {
    const labels: ListLabels = { priority: { High: { hidden: true } } };
    expect(listOptions('priority', PRIORITIES, labels, t).map((o) => o.value)).toEqual(['Low', 'Medium', 'Urgent']);
    expect(listOptions('priority', PRIORITIES, labels, t, 'High').map((o) => o.value)).toEqual(['Low', 'Medium', 'High', 'Urgent']);
  });
  it('labels with the translation key of the list', () => {
    expect(listOptions('assetStatus', ['Active'], {}, t)).toEqual([{ value: 'Active', label: 'status.Active' }]);
  });
  it('uses the organization\'s colour, else the screen\'s', () => {
    expect(listColor('priority', 'Urgent', { priority: { Urgent: { color: 'grape' } } }, 'red')).toBe('grape');
    expect(listColor('priority', 'High', { priority: { Urgent: { color: 'grape' } } }, 'orange')).toBe('orange');
  });
  it('falls back to the first visible value when the default is hidden', () => {
    expect(firstVisible('priority', PRIORITIES, { priority: { Medium: { hidden: true }, Urgent: { order: 0 } } }, 'Medium')).toBe('Urgent');
    expect(firstVisible('priority', PRIORITIES, {}, 'Medium')).toBe('Medium');
  });
  it('overrides the words in both languages, then restores them', async () => {
    const i18n = i18next.createInstance();
    await i18n.init({ lng: 'en', resources: { en: { translation: { priority: { Urgent: 'Urgent' } } }, ar: { translation: { priority: { Urgent: 'عاجلة' } } } } });
    applyListLabels(i18n, { priority: { Urgent: { name: 'Emergency', nameAr: 'طارئ' } } });
    expect(i18n.t('priority.Urgent')).toBe('Emergency');
    expect(i18n.getFixedT('ar')('priority.Urgent')).toBe('طارئ');
    applyListLabels(i18n, {});
    expect(i18n.t('priority.Urgent')).toBe('Urgent');
    expect(i18n.getFixedT('ar')('priority.Urgent')).toBe('عاجلة');
  });
});
