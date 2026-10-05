import { expect, it } from 'vitest';
import { KanbanDragMeasurement } from './kanbanDragMeasurement';

it('retains a windowed drag origin through intermediate scroll measurements and resets on the next drag', () => {
  const row = document.createElement('div'); row.dataset.boardWindowId = 'source';
  const node = document.createElement('div'); row.append(node);
  let top = 80;
  node.getBoundingClientRect = () => ({ x: 0, y: top, left: 0, top, right: 288, bottom: top + 100, width: 288, height: 100, toJSON() {} });
  const measurement = new KanbanDragMeasurement(); measurement.start('card:source');
  const origin = measurement.measure(node);
  top -= 20;
  expect(measurement.measure(node)).toBe(origin);
  top -= 89;
  expect(measurement.measure(node)).toBe(origin);
  measurement.finish();
  expect(measurement.measure(node).top).toBe(-29);
  measurement.start('card:source');
  expect(measurement.measure(node).top).toBe(-29);
});

it('measures ordinary sources, unrelated targets, replacement nodes and dimension changes freshly', () => {
  const measurement = new KanbanDragMeasurement(); measurement.start('source');
  const node = document.createElement('div'); let top = 80, width = 288;
  node.getBoundingClientRect = () => ({ x: 0, y: top, left: 0, top, right: width, bottom: top + 100, width, height: 100, toJSON() {} });
  measurement.measure(node); top = 60;
  expect(measurement.measure(node).top).toBe(60);
  const row = document.createElement('div'); row.dataset.boardWindowId = 'other'; row.append(node);
  measurement.measure(node); top = 40;
  expect(measurement.measure(node).top).toBe(40);
  row.dataset.boardWindowId = 'source'; const origin = measurement.measure(node);
  top = 20; width = 320;
  expect(measurement.measure(node)).not.toBe(origin);
  expect(measurement.measure(node).width).toBe(320);
  const replacement = node.cloneNode() as HTMLElement; replacement.getBoundingClientRect = node.getBoundingClientRect; row.replaceChildren(replacement);
  top = 0;
  expect(measurement.measure(replacement).top).toBe(0);
});
