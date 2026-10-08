import { StrictMode } from 'react';
import { Button, CssBaseline, TextField, ThemeProvider } from '@mui/material';
import { fireEvent, render, screen } from '@testing-library/react';
import { appTheme } from './appTheme';

const autofillRules = () => [...document.styleSheets].flatMap(sheet => [...sheet.cssRules])
  .filter(rule => rule.type === CSSRule.KEYFRAMES_RULE && /^mui-auto-fill(?:-cancel)?$/.test((rule as CSSKeyframesRule).name))
  .map(rule => (rule as CSSKeyframesRule).name).sort();

it('keeps shared autofill rules across field mount/unmount and preserves filled-label detection', () => {
  const fields = (count: number) => <StrictMode><ThemeProvider theme={appTheme}><CssBaseline />
    {Array.from({ length: count }, (_, index) => <TextField key={index} label={`Field ${index + 1}`} />)}
  </ThemeProvider></StrictMode>;
  const { rerender } = render(fields(0));
  // Emotion emits standard and WebKit rules; jsdom normalizes their cssText.
  const shared = ['mui-auto-fill', 'mui-auto-fill', 'mui-auto-fill-cancel', 'mui-auto-fill-cancel'];
  expect(autofillRules()).toEqual(shared);
  rerender(fields(5));
  expect(autofillRules()).toEqual(shared);
  const input = screen.getByRole('textbox', { name: 'Field 1' });
  const label = document.querySelector(`label[for="${input.id}"]`)!;
  expect(label).not.toHaveClass('MuiInputLabel-shrink');
  // jsdom has WebkitAnimation but no AnimationEvent; React selects this name.
  const autofill = new Event('webkitAnimationStart', { bubbles: true });
  Object.defineProperty(autofill, 'animationName', { value: 'mui-auto-fill' });
  fireEvent(input, autofill);
  expect(label).toHaveClass('MuiInputLabel-shrink');
  const cancelled = new Event('webkitAnimationStart', { bubbles: true });
  Object.defineProperty(cancelled, 'animationName', { value: 'mui-auto-fill-cancel' });
  fireEvent(input, cancelled);
  expect(label).not.toHaveClass('MuiInputLabel-shrink');
  rerender(fields(0));
  expect(autofillRules()).toEqual(shared);
  rerender(fields(2));
  expect(autofillRules()).toEqual(shared);
});


it('applies interactive label and button colors without interpolating disabled colors', () => {
  const controls = (disabled: boolean) => <ThemeProvider theme={appTheme}><CssBaseline />
    <TextField label="Account email" disabled={disabled} />
    <Button variant="contained" disabled={disabled}>Submit account</Button>
  </ThemeProvider>;
  const { rerender } = render(controls(true));
  rerender(controls(false));
  const input = screen.getByRole('textbox', { name: 'Account email' });
  const label = document.querySelector(`label[for="${input.id}"]`)!;
  const button = screen.getByRole('button', { name: 'Submit account' });
  expect(input).toBeEnabled(); expect(button).toBeEnabled();
  expect(getComputedStyle(label).transition).toContain('transform');
  expect(getComputedStyle(label).transition).not.toMatch(/(?:^|[, ])(?:color|all) /);
  expect(getComputedStyle(button).transition).toContain('box-shadow');
  expect(getComputedStyle(button).transition).not.toMatch(/(?:^|[, ])(?:background-color|color|all) /);
});
