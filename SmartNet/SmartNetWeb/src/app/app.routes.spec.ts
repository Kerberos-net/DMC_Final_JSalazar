import { Route } from '@angular/router';
import { routes } from './app.routes';
import { ShellLayout } from './shared/shell-layout/shell-layout';

/**
 * Structural guard for the "login has no app shell" decision: `/login` must be a top-level route,
 * never nested under the `ShellLayout` parent, so it never inherits the sidebar (marca, nav,
 * theme control, profile). The authenticated screens (bandeja, detalle, configuracion) must be
 * children of that parent.
 */
describe('app.routes', () => {
  const shellParent = routes.find((r) => r.component === ShellLayout);
  const childPaths = (shellParent?.children ?? []).map((c) => c.path);

  it('exposes /login as a top-level route outside the shell', () => {
    const login = routes.find((r) => r.path === 'login');
    expect(login).toBeDefined();
    expect(childPaths).not.toContain('login');
  });

  it('nests the authenticated screens under the ShellLayout parent', () => {
    expect(shellParent).toBeDefined();
    expect(childPaths).toEqual(expect.arrayContaining(['bandeja', 'detalle/:id', 'configuracion']));
  });

  it('keeps the auth guard on every authenticated child', () => {
    const guarded = (shellParent?.children ?? []).filter(
      (c: Route) => c.path !== '' && (c.canActivate?.length ?? 0) > 0
    );
    expect(guarded.map((c) => c.path)).toEqual(
      expect.arrayContaining(['bandeja', 'detalle/:id', 'configuracion'])
    );
  });

  // BACKLOG #22 PR4 (spa spec req 1) -- the catalog screens are additive lazy ShellLayout
  // children under the grouped `catalogos/` prefix, each behind `authGuard`. Additive: the
  // `arrayContaining` assertions above still hold.
  it('registers catalogos/plan-contable as a guarded lazy child of the shell', () => {
    const ruta = (shellParent?.children ?? []).find(
      (c: Route) => c.path === 'catalogos/plan-contable'
    );
    expect(ruta).toBeDefined();
    expect((ruta?.canActivate?.length ?? 0) > 0).toBe(true);
    expect(typeof ruta?.loadComponent).toBe('function');
  });

  // BACKLOG #22 PR6 (spa spec req 1,2) -- additive guarded lazy child; the `arrayContaining`
  // assertions above still hold.
  it('registers catalogos/proveedores as a guarded lazy child of the shell', () => {
    const ruta = (shellParent?.children ?? []).find(
      (c: Route) => c.path === 'catalogos/proveedores'
    );
    expect(ruta).toBeDefined();
    expect((ruta?.canActivate?.length ?? 0) > 0).toBe(true);
    expect(typeof ruta?.loadComponent).toBe('function');
  });
});
