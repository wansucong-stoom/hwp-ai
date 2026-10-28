import test from 'node:test';
import assert from 'node:assert/strict';
import { validateTableDesign, validateTableSnapshot } from '../table-design-plan.mjs';

const cell = (id, row, column, rowSpan = 1, columnSpan = 1, text = '') => ({ id, row, column, rowSpan, columnSpan, text, style: {} });
const snapshot = () => ({ referenceTableId: 'ref', tables: [
  { id: 'ref', rows: 1, columns: 3, cells: [cell('left', 0, 0), cell('mid', 0, 1), cell('right', 0, 2)] },
  { id: 'target', rows: 3, columns: 3, cells: [
    cell('header', 0, 0, 1, 3, '머리행'),
    cell('a', 1, 0), cell('vertical', 1, 1, 2, 1), cell('b', 1, 2),
    cell('c', 2, 0), cell('d', 2, 2)
  ] }
] });
const assignment = cellId => ({ tableId: 'target', cellId, role: '본문', fillSource: 'mid', paragraphSource: 'mid', characterSource: 'mid', edges: { left: 'left', right: 'right', top: 'mid', bottom: 'mid' } });
const plan = () => ({ assignments: ['header', 'a', 'vertical', 'b', 'c', 'd'].map(assignment), uncertainties: [] });

test('independent left and right sources support a full-width merged cell', () => {
  const s = snapshot(), p = plan();
  p.assignments[0].role = '머리행';
  assert.equal(validateTableDesign(s, p), p);
  assert.equal(p.assignments[0].edges.left, 'left');
  assert.equal(p.assignments[0].edges.right, 'right');
});
test('rejects missing, duplicate or invented targets and sources', () => {
  for (const mutate of [
    p => p.assignments.pop(),
    p => p.assignments.push(p.assignments[0]),
    p => { p.assignments[0].cellId = 'unknown'; },
    p => { p.assignments[0].edges.right = 'unknown'; },
    p => { p.assignments[0].text = 'replacement'; }
  ]) { const p = plan(); mutate(p); assert.throws(() => validateTableDesign(snapshot(), p)); }
});
test('rejects overlapping or incomplete physical grids', () => {
  const a = snapshot(); a.tables[1].cells[0].rowSpan = 2;
  assert.throws(() => validateTableSnapshot(a), /Overlapping/);
  const b = snapshot(); b.tables[1].cells.pop();
  assert.throws(() => validateTableSnapshot(b), /Incomplete/);
});
