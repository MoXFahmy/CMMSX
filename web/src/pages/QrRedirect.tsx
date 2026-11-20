import { Navigate, useParams } from 'react-router';

/** A QR label scanned with an ordinary phone camera opens here (after sign-in) and goes on to the record. */
export function QrRedirect() {
  const { type, id = '' } = useParams();
  const to = type === 'asset' ? `/assets/${id}` : type === 'location' ? `/locations?loc=${id}` : type === 'part' ? `/inventory/parts/${id}` : '/';
  return <Navigate to={to} replace />;
}
