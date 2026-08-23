import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { vi } from 'vitest';
import { authGuard } from './auth.guard';
import { SessionService } from './session.service';

describe('authGuard', () => {
  let createUrlTreeSpy: ReturnType<typeof vi.fn>;
  let verificarSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    verificarSpy = vi.fn();
    createUrlTreeSpy = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        { provide: SessionService, useValue: { verificar: verificarSpy } },
        { provide: Router, useValue: { createUrlTree: createUrlTreeSpy } },
      ],
    });
  });

  it('redirects to /login when the session check fails', async () => {
    verificarSpy.mockResolvedValue(false);
    const loginTree = {} as UrlTree;
    createUrlTreeSpy.mockReturnValue(loginTree);

    const resultado = await TestBed.runInInjectionContext(() =>
      authGuard({} as never, {} as never)
    );

    expect(verificarSpy).toHaveBeenCalled();
    expect(createUrlTreeSpy).toHaveBeenCalledWith(['/login']);
    expect(resultado).toBe(loginTree);
  });

  it('allows the navigation when the session check succeeds', async () => {
    verificarSpy.mockResolvedValue(true);

    const resultado = await TestBed.runInInjectionContext(() =>
      authGuard({} as never, {} as never)
    );

    expect(resultado).toBe(true);
    expect(createUrlTreeSpy).not.toHaveBeenCalled();
  });
});
