import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * BACKLOG #22, design D8 -- source-agnostic pagination chrome. Driven purely by the
 * `PaginaBandeja<T>` fields (`pagina`, `totalPaginas`, `tamanioPagina`); it fetches nothing. The
 * proveedores container maps the outputs to a server re-query; the plan-contable / tipo-de-cambio
 * containers map them to a client-side `computed()` slice. Changing the rows-per-page always
 * resets to page 1 (`tamanioChange` + `paginaChange(1)`), matching the screens' "reset page on
 * filter/sort change" rule.
 */
@Component({
  selector: 'app-tabla-paginador',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tabla-paginador.html',
  styleUrl: './tabla-paginador.css',
})
export class TablaPaginador {
  readonly pagina = input.required<number>();
  readonly totalPaginas = input.required<number>();
  readonly tamanio = input.required<number>();
  /** Canvas rows-per-page set; overridable but defaulted so screens need not repeat it. */
  readonly tamaniosDisponibles = input<readonly number[]>([6, 10, 20, 50]);

  readonly paginaChange = output<number>();
  readonly tamanioChange = output<number>();

  protected readonly enPrimera = computed(() => this.pagina() <= 1);
  protected readonly enUltima = computed(() => this.pagina() >= this.totalPaginas());

  anterior(): void {
    if (!this.enPrimera()) {
      this.paginaChange.emit(this.pagina() - 1);
    }
  }

  siguiente(): void {
    if (!this.enUltima()) {
      this.paginaChange.emit(this.pagina() + 1);
    }
  }

  onTamanio(valor: string): void {
    this.tamanioChange.emit(Number(valor));
    this.paginaChange.emit(1);
  }
}
