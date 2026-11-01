import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { catchError, EMPTY, exhaustMap, finalize, forkJoin, interval, map, Observable, of, Subscription, switchMap, tap, throwError } from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { Order } from '@app/models/orders/order.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';

const SYNC_MS = 15_000;

@Component({
  selector: 'app-group-order-page',
  imports: [ParticipantCardComponent, CurrencyPipe, DatePipe, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly groupOrders = inject(GroupOrdersService);
  private readonly preOrders = inject(PreOrdersService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';
  private syncStarted = false;
  private syncStopped = false;
  private refreshInFlight = false;
  private syncSubscription: Subscription | null = null;

  readonly loading = signal(true);
  readonly errorStatus = signal<number | null>(null);
  readonly invite = signal<GuestInvite | null>(null);
  readonly sessions = signal<GroupSessionsResponse | null>(null);
  readonly ownerSessionId = signal('');
  readonly groupStatus = signal<GroupStatusResponse | null>(null);
  readonly updatedAt = signal<Date | null>(null);
  readonly refreshFailed = signal(false);
  readonly submitting = signal(false);
  readonly submitOpen = signal(false);
  readonly removing = signal(false);
  readonly removeTarget = signal<Participant | null>(null);

  readonly participants = computed(() => this.sessions()?.participants ?? []);
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly hasAnyDish = computed(() => this.participants().some((person) => person.items.length > 0));
  readonly isReadOnly = computed(() => this.groupStatus()?.status !== 'Open');
  readonly canSubmit = computed(() => this.consolidated().items.length > 0 && !this.submitting() && !this.isReadOnly());
  readonly badgeLabel = computed(() => {
    switch (this.groupStatus()?.status) {
      case 'Open':
        return 'Collecting pre-orders';
      case 'Submitted':
        return 'Sent to Restaurant';
      default:
        return '';
    }
  });
  readonly guestLabel = computed(() => {
    const status = this.groupStatus();
    if (!status) {
      return '';
    }
    return `${status.currentParticipants} of ${status.maxCapacity} guests`;
  });
  readonly pendingLabel = computed(() => {
    const waiting = this.participants().filter((person) => !person.isSubmitted).length;
    if (waiting === 0 || this.isReadOnly()) {
      return '';
    }
    const subject = waiting === 1 ? "participant hasn't" : "participants haven't";
    return `${waiting} ${subject} submitted their cart yet — their dishes won't be included in the order`;
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.errorStatus.set(null);

    this.groupOrders
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          this.groupOrders.getOwnerSessionId$(this.reservationId, { silent: true }).pipe(
            catchError((error: unknown) => {
              if (error instanceof HttpErrorResponse && error.status === 404) {
                return of('');
              }
              return throwError(() => error);
            }),
            switchMap((ownerSessionId) =>
              forkJoin({
                invite: of(invite),
                sessions: this.groupOrders.getSessions$(invite.GroupReservationId),
                status: this.groupOrders.getStatus$(invite.GroupReservationId),
              }).pipe(
                switchMap((result) =>
                  this.includeOwnerOrder(result.sessions, ownerSessionId, invite).pipe(
                    map((merged) => ({ ...result, ...merged })),
                  ),
                ),
              ),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.invite.set(result.invite);
          this.sessions.set(result.sessions);
          this.ownerSessionId.set(result.ownerSessionId);
          this.groupStatus.set(result.status);
          this.updatedAt.set(new Date());
          this.refreshFailed.set(false);
          this.loading.set(false);
          this.startSync();
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.errorStatus.set(error instanceof HttpErrorResponse ? error.status : 0);
        },
      });
  }

  refresh(): void {
    this.refresh$(true).subscribe();
  }

  openSubmit(): void {
    if (!this.canSubmit()) {
      return;
    }
    this.submitOpen.set(true);
  }

  confirmSubmit(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.groupOrders
      .submit$(groupId, this.reservationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Pre-order sent to the restaurant');
          this.groupStatus.update((status) => (status ? { ...status, status: 'Submitted' } : status));
          this.submitting.set(false);
          this.submitOpen.set(false);
          this.stopSync();
        },
        error: () => {
          this.submitting.set(false);
          this.submitOpen.set(false);
        },
      });
  }

  askRemove(person: Participant): void {
    this.removeTarget.set(person);
  }

  confirmRemove(): void {
    const person = this.removeTarget();
    const groupId = this.invite()?.GroupReservationId;
    if (!person || !groupId || this.removing()) {
      return;
    }

    this.removing.set(true);
    this.groupOrders
      .removeSession$(groupId, person.id)
      .pipe(
        switchMap((updated) => this.includeOwnerOrder(updated, this.ownerSessionId(), this.invite())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => {
          this.sessions.set(updated.sessions);
          this.ownerSessionId.set(updated.ownerSessionId);
          this.groupStatus.update((status) =>
            status ? { ...status, currentParticipants: updated.sessions.participants.length } : status,
          );
          this.removing.set(false);
          this.removeTarget.set(null);
        },
        error: () => {
          this.removing.set(false);
          this.removeTarget.set(null);
        },
      });
  }

  private includeOwnerOrder(
    sessions: GroupSessionsResponse,
    ownerSessionId: string,
    invite: GuestInvite | null,
  ): Observable<{ sessions: GroupSessionsResponse; ownerSessionId: string }> {
    const hasOwner = sessions.participants.some(
      (person) => person.type === 'Owner' || (ownerSessionId !== '' && person.id === ownerSessionId),
    );
    if (hasOwner) {
      return of({ sessions, ownerSessionId });
    }

    return this.preOrders.getByReservationId(this.reservationId).pipe(
      map((order) => {
        const owner = this.ownerFromOrder(order, ownerSessionId, invite);
        return {
          ownerSessionId: owner.id,
          sessions: {
            participants: [owner, ...sessions.participants],
            total: sessions.total + owner.total,
          },
        };
      }),
      catchError(() => of({ sessions, ownerSessionId })),
    );
  }

  private ownerFromOrder(order: Order, ownerSessionId: string, invite: GuestInvite | null): Participant {
    const items = order.orderItems.map((item) => ({
      dish: {
        id: item.dish.id,
        name: item.dish.name,
        price: item.dish.price,
        imageUrl: item.dish.imageUrl,
        weight: item.dish.weight,
      },
      quantity: item.quantity,
    }));
    const total = items.reduce((sum, item) => sum + item.dish.price * item.quantity, 0);

    return {
      id: ownerSessionId || order.orderId,
      type: 'Owner',
      name: invite?.InviterName || 'You',
      items,
      total,
      isSubmitted: order.status === 1 || order.status === 3,
    };
  }

  private startSync(): void {
    if (this.syncStarted || this.syncStopped || this.groupStatus()?.status !== 'Open') {
      return;
    }

    this.syncStarted = true;
    this.syncSubscription = interval(SYNC_MS)
      .pipe(
        exhaustMap(() => this.refresh$(true)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  private stopSync(): void {
    this.syncStopped = true;
    this.syncSubscription?.unsubscribe();
    this.syncSubscription = null;
  }

  private refresh$(silent: boolean) {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || this.refreshInFlight) {
      return EMPTY;
    }

    this.refreshInFlight = true;
    const options = silent ? { silent: true as const } : undefined;

    return forkJoin({
      sessions: this.groupOrders.getSessions$(groupId, options),
      status: this.groupOrders.getStatus$(groupId, options),
    }).pipe(
      switchMap(({ sessions, status }) =>
        this.includeOwnerOrder(sessions, this.ownerSessionId(), this.invite()).pipe(
          map((merged) => ({ status, ...merged })),
        ),
      ),
      tap(({ sessions, status, ownerSessionId }) => {
        if (this.syncStopped) {
          return;
        }
        this.sessions.set(sessions);
        this.ownerSessionId.set(ownerSessionId);
        this.groupStatus.set(status);
        this.updatedAt.set(new Date());
        this.refreshFailed.set(false);
        if (status.status !== 'Open') {
          this.stopSync();
        }
      }),
      catchError(() => {
        if (silent) {
          this.refreshFailed.set(true);
        }
        return of(null);
      }),
      finalize(() => {
        this.refreshInFlight = false;
      }),
    );
  }
}
