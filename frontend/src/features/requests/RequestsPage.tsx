// "Requests and reports": the user's own requests from the API, followed by a sample history.
// The two buttons open the forms; a request that is filed appears at the top of the table.
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, describeError } from '../../shared/api/client'
import type { EmployeeRequest } from '../../shared/api/types'
import { formatDate } from '../../shared/time/zurich'
import { Notice, Pagination, SkeletonRows } from '../../shared/ui/components'
import { PlusIcon, SickIcon } from '../../shared/ui/icons'
import { RequestDetailsDialog } from './RequestDetailsDialog'
import { RequestDialog, REQUESTS_KEY } from './RequestDialog'
import type { RequestFormKind } from './RequestDialog'
import { describeType, formatRequestNumber, SAMPLE_REQUESTS, STATUS_CLASSES, STATUS_LABELS } from './requestTexts'
import './requests.css'

const PAGE_SIZE = 5

interface RequestsTableProps {
  requests: EmployeeRequest[]
  onOpen: (request: EmployeeRequest) => void
}

function RequestsTable({ requests, onOpen }: RequestsTableProps) {
  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="visually-hidden">בקשות ודיווחים</caption>
        <thead>
          <tr>
            <th scope="col">תיאור</th>
            <th scope="col">סטטוס</th>
            <th scope="col">תאריך</th>
            <th scope="col">סוג בקשה</th>
            <th scope="col">מספר בקשה</th>
          </tr>
        </thead>
        <tbody>
          {requests.map((request) => (
            <tr key={request.id}>
              {/* React escapes text, so a title is always shown as plain text, never as HTML.
                  dir="auto" lets a title that starts with English or a number read in its own direction. */}
              <td className="request-title">
                <button type="button" className="link-button" dir="auto" onClick={() => onOpen(request)}>
                  {request.title}
                </button>
              </td>
              <td>
                <span className={`badge ${STATUS_CLASSES[request.status]}`}>{STATUS_LABELS[request.status]}</span>
              </td>
              <td className="latin">{formatDate(request.createdAtUtc)}</td>
              <td>{describeType(request)}</td>
              <td className="latin">{formatRequestNumber(request.id)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export default function RequestsPage() {
  const own = useQuery({
    queryKey: REQUESTS_KEY,
    queryFn: () => api<{ items: EmployeeRequest[] }>('GET', '/requests'),
  })
  const [page, setPage] = useState(1)
  const [openForm, setOpenForm] = useState<RequestFormKind | null>(null)
  const [shownRequest, setShownRequest] = useState<EmployeeRequest | null>(null)
  const requests = [...(own.data?.items ?? []), ...SAMPLE_REQUESTS]
  const totalPages = Math.ceil(requests.length / PAGE_SIZE)
  const shownPage = Math.min(page, totalPages)

  function closeForm() {
    setOpenForm(null)
    // A request that was just filed is at the top of the first page.
    setPage(1)
  }

  return (
    <>
      <div className="page-header">
        <h1>בקשות ודיווחים</h1>
        <span className="row">
          <button type="button" className="button button-primary" onClick={() => setOpenForm('request')}>
            <PlusIcon />
            בקשה חדשה
          </button>
          <button type="button" className="button button-dark" onClick={() => setOpenForm('sickness')}>
            <SickIcon />
            דיווח מחלה
          </button>
        </span>
      </div>
      <section className="card" aria-label="הבקשות שלי">
        {own.isError ? (
          <Notice>{describeError(own.error)}</Notice>
        ) : own.isPending ? (
          <SkeletonRows rows={PAGE_SIZE} />
        ) : (
          <>
            <RequestsTable requests={requests.slice((shownPage - 1) * PAGE_SIZE, shownPage * PAGE_SIZE)} onOpen={setShownRequest} />
            <Pagination page={shownPage} totalPages={totalPages} totalItems={requests.length} onPageChange={setPage} />
          </>
        )}
      </section>
      {openForm && <RequestDialog key={openForm} kind={openForm} onClose={closeForm} />}
      {shownRequest && <RequestDetailsDialog key={shownRequest.id} request={shownRequest} onClose={() => setShownRequest(null)} />}
    </>
  )
}
