/**
 * The cost lines of a work order's totals card (0.17.0): labour (staff), parts, other, and contractor when there is any — contractor lines plus
 * company-linked labour, which the server moves out of labour — so the lines always add up to the total.
 */
export function costLines(t: { laborCost: number; partsCost: number; otherCost: number; contractorCost: number; totalCost: number }): { key: string; value: number }[] {
  return [
    { key: 'wo.laborCost', value: t.laborCost }, { key: 'wo.partsCost', value: t.partsCost }, { key: 'wo.otherCost', value: t.otherCost },
    ...(t.contractorCost ? [{ key: 'contractors.cost', value: t.contractorCost }] : []),
  ];
}
