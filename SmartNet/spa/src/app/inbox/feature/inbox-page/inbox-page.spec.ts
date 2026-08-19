import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { InboxPage } from './inbox-page';

describe('InboxPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InboxPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('loads the bandeja on init with the default order (desc) and no estado filter', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'desc'
    );
    expect(req.request.params.has('estado')).toBe(false);
    req.flush([]);
  });

  it('re-fetches with the new estado when the filter control emits a change', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush([]);

    fixture.componentInstance.onEstadoChange('DESCARTADO');
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('estado') === 'DESCARTADO'
    );
    req.flush([]);
  });

  it('re-fetches with the new orden when the sort control emits a change', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush([]);

    fixture.componentInstance.onOrdenChange('asc');
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'asc'
    );
    req.flush([]);
  });

  it('never renders an approve/edit/re-trigger control', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush([]);
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('button, [role="button"]');
    expect(buttons.length).toBe(0);
  });
});
