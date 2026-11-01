import {
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../services/toast/toast.service';
import { inject } from '@angular/core';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { getServerErrorMessage } from '../shared/helpers/http-error-helpers';

export const tokenInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
) => {
  const toastr = inject(ToastService);

  // Anonymous guest links (/guest/sessions, /guest/invites) carry their own session
  // token in the body or path. Do not attach the Cognito id token — that breaks
  // POST /guest/sessions. Owner routes under /guest/groups require Cognito, so
  // they fall through and receive the logged-in user's token.
  const isAnonymousGuestRequest =
    req.url.includes('/guest/sessions') || req.url.includes('/guest/invites/');

  if (isAnonymousGuestRequest) {
    return next(req).pipe(
      catchError((error: HttpErrorResponse) => {
        console.log(error);

        let errorMessage = 'An unexpected error occurred.';
        if (error.error && typeof error.error === 'string') {
          errorMessage = error.error;
        } else if (error.error?.error) {
          errorMessage = error.error.error;
        } else if (error.error?.message) {
          errorMessage = error.error.message;
        }

        // For guest endpoints, you may still show toast if you want:
        switch (error.status) {
          case 400:
            toastr.error(errorMessage || 'Bad request.', 'Error');
            break;
          case 401:
            toastr.error('Session expired. Please re-open the invite link.', 'Unauthorized');
            break;
          case 403:
            toastr.error(errorMessage || 'You are not allowed!', 'Forbidden');
            break;
          case 404:
            toastr.error(errorMessage || 'Resource not found.', 'Not Found');
            break;
          case 409:
            toastr.error(errorMessage || 'Conflict occurred.', 'Conflict');
            break;
          case 422:
            toastr.error(errorMessage || 'Validation error.', 'Error');
            break;
          case 500:
            toastr.error('Server error. Please try again later.', 'Server Error');
            break;
          default:
            toastr.error(errorMessage, 'Error');
            break;
        }

        return throwError(() => error);
      })
    );
  }

  const authService = inject(AuthenticationService);
  const tokens = authService.getTokens();

  const clonedRequest =
    tokens && tokens.idToken
      ? req.clone({
        setHeaders: {
          Authorization: 'Bearer ' + tokens.idToken,
        },
      })
      : req;

  return next(clonedRequest).pipe(
    catchError((error: HttpErrorResponse) => {
      console.log(error);
      const errorMessage = getServerErrorMessage(error) || 'An unexpected error occurred.';

      if (clonedRequest.url.includes('orders') && error.status === 404) {
        return throwError(() => error);
      }

      if (clonedRequest.context.get(SKIP_ERROR_TOAST)) {
        return throwError(() => error);
      }

      switch (error.status) {
        case 400:
          toastr.error(errorMessage || 'Bad request.', 'Error');
          break;
        case 401:
          toastr.error('Please log in to perform the action!', 'Unauthorized');
          break;
        case 403:
          toastr.error(errorMessage || 'You are not allowed!', 'Forbidden');
          break;
        case 404:
          toastr.error(errorMessage || 'Resource not found.', 'Not Found');
          break;
        case 409:
          toastr.error(errorMessage || 'Conflict occurred.', 'Conflict');
          break;
        case 422:
          toastr.error(errorMessage || 'Validation error.', 'Error');
          break;
        case 500:
          toastr.error('Server error. Please try again later.', 'Server Error');
          break;
        default:
          toastr.error(errorMessage, 'Error');
          break;
      }

      return throwError(() => error);
    })
  );
};