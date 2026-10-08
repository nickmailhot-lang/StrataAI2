import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`ARCH-02: shared input styles retain autofill detection across form navigation at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    const shared = ['mui-auto-fill', 'mui-auto-fill', 'mui-auto-fill-cancel', 'mui-auto-fill-cancel'];
    const rules = () => page.evaluate(() => Array.from(document.styleSheets).flatMap(sheet => Array.from(sheet.cssRules))
      .filter(rule => rule.type === CSSRule.KEYFRAMES_RULE && /^mui-auto-fill(?:-cancel)?$/.test((rule as CSSKeyframesRule).name))
      .map(rule => (rule as CSSKeyframesRule).name).sort());
    await page.goto('/login');
    const email = page.getByRole('textbox', { name: 'Email', exact: true });
    await expect(email).toBeEnabled();
    expect(await rules()).toEqual(shared);
    expect(await email.evaluate(node => getComputedStyle(node).animationName)).toBe('mui-auto-fill-cancel');
    expect(await email.evaluate(node => getComputedStyle(node).animationDuration)).toBe('0.01s');
    expect(await page.evaluate(() => Array.from(document.styleSheets).flatMap(sheet => Array.from(sheet.cssRules))
      .some(rule => rule.type === CSSRule.STYLE_RULE && (rule as CSSStyleRule).selectorText.includes(':-webkit-autofill')
        && (rule as CSSStyleRule).style.animationName === 'mui-auto-fill'))).toBe(true);
    const emailId = await email.getAttribute('id');
    const label = page.locator('label').filter({ hasText: /^Email/ }).filter({ visible: true });
    await expect(label).toHaveAttribute('for', emailId!);
    await expect(label).toHaveAttribute('data-shrink', 'false');
    // Synthetic animation notifications test the MUI listener, not a password manager.
    await email.evaluate(node => node.dispatchEvent(new AnimationEvent('animationstart', { bubbles: true, animationName: 'mui-auto-fill' })));
    await expect(label).toHaveAttribute('data-shrink', 'true');
    await email.evaluate(node => node.dispatchEvent(new AnimationEvent('animationstart', { bubbles: true, animationName: 'mui-auto-fill-cancel' })));
    await expect(label).toHaveAttribute('data-shrink', 'false');
    await page.getByRole('tab', { name: 'Register', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Display name' })).toBeEnabled();
    expect(await rules()).toEqual(shared);
    await page.getByRole('tab', { name: 'Sign in', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Display name' })).toHaveCount(0);
    expect(await rules()).toEqual(shared);
    await page.getByRole('link', { name: 'Forgot password?' }).click();
    await expect(page.getByRole('button', { name: 'Request reset', exact: true })).toBeEnabled();
    expect(await rules()).toEqual(shared);
    await page.goto('/login');
    await expect(page.getByRole('textbox', { name: 'Email', exact: true })).toBeEnabled();
    expect(await rules()).toEqual(shared);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
