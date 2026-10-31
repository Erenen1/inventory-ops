import { PackagePlus } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { ActiveBadge } from '@/components/shared/badges';
import { EmptyState, ErrorState, LoadingRows, PageHeader, Pagination, SearchInput } from '@/components/shared/page';
import { SortableHeader } from '@/components/shared/sortable-header';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/form-controls';
import { Dialog, DialogTrigger } from '@/components/ui/overlays';
import { Card, Table, Td, Th } from '@/components/ui/surface';
import { useCurrentUser } from '@/features/auth/session';
import { useCategories, useProducts } from '@/features/products/api';
import { ProductFormDialog } from '@/features/products/ProductFormDialog';
import { useListParams } from '@/hooks/use-list-params';
import { formatMoney, formatNumber, formatRelative } from '@/lib/format';
import { cn } from '@/lib/utils';

const PAGE_SIZE = 20;

export function ProductsPage() {
  const user = useCurrentUser();
  const navigate = useNavigate();
  const list = useListParams({ sortBy: 'name', sortDirection: 'Asc' });
  const categories = useCategories();
  const [createOpen, setCreateOpen] = useState(false);
  const status = list.filter('status');

  const { data, isLoading, isFetching, error, refetch } = useProducts({
    search: list.appliedSearch,
    categoryId: list.filter('categoryId') || undefined,
    isActive: status === '' ? undefined : status === 'active',
    sortBy: list.sortBy,
    sortDirection: list.sortDirection,
    page: list.page,
    pageSize: PAGE_SIZE,
  });

  const sortProps = { sortBy: list.sortBy, sortDirection: list.sortDirection, onSort: list.toggleSort };
  const filtered = !!list.appliedSearch || !!list.filter('categoryId') || !!status;

  return (
    <>
      <PageHeader
        title="Products"
        description="Catalog items, prices and live availability."
        actions={
          user.permissions.canOperate && (
            <Dialog open={createOpen} onOpenChange={setCreateOpen}>
              <DialogTrigger asChild>
                <Button>
                  <PackagePlus /> New product
                </Button>
              </DialogTrigger>
              {createOpen && (
                <ProductFormDialog
                  onSaved={(id) => {
                    setCreateOpen(false);
                    void navigate(`/products/${id}`);
                  }}
                />
              )}
            </Dialog>
          )
        }
      />

      <Card>
        <div className="flex flex-col gap-3 border-b border-zinc-100 p-4 sm:flex-row sm:items-center">
          <SearchInput
            label="Search products"
            placeholder="Search by name or SKU"
            value={list.search}
            onChange={list.setSearch}
          />
          <div className="flex gap-3">
            <Select
              aria-label="Filter by category"
              className="sm:w-48"
              value={list.filter('categoryId')}
              onChange={(e) => {
                list.setFilter('categoryId', e.target.value);
              }}
            >
              <option value="">All categories</option>
              {categories.data?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
            <Select
              aria-label="Filter by status"
              className="sm:w-36"
              value={status}
              onChange={(e) => {
                list.setFilter('status', e.target.value);
              }}
            >
              <option value="">Any status</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </Select>
          </div>
        </div>

        {error ? (
          <ErrorState error={error} onRetry={() => void refetch()} />
        ) : (
          <>
            <Table aria-busy={isFetching}>
              <thead>
                <tr>
                  <SortableHeader field="sku" label="SKU" {...sortProps} />
                  <SortableHeader field="name" label="Name" {...sortProps} />
                  <Th className="hidden md:table-cell">Category</Th>
                  <SortableHeader field="price" label="Price" align="right" {...sortProps} />
                  <SortableHeader field="available" label="Available" align="right" {...sortProps} />
                  <Th>Status</Th>
                  <SortableHeader field="updatedAt" label="Updated" {...sortProps} />
                </tr>
              </thead>
              <tbody className={cn(isFetching && !isLoading && 'opacity-60 transition-opacity')}>
                {isLoading ? (
                  <LoadingRows columns={7} />
                ) : (
                  data?.items.map((p) => (
                    <tr
                      key={p.id}
                      className="cursor-pointer hover:bg-zinc-50"
                      onClick={() => void navigate(`/products/${p.id}`)}
                    >
                      <Td className="font-mono text-xs text-zinc-600">{p.sku}</Td>
                      <Td>
                        <Link
                          to={`/products/${p.id}`}
                          onClick={(e) => {
                            e.stopPropagation();
                          }}
                          className="font-medium text-zinc-900 hover:underline"
                        >
                          {p.name}
                        </Link>
                      </Td>
                      <Td className="hidden md:table-cell">{p.categoryName}</Td>
                      <Td className="tabular text-right">{formatMoney(p.unitPrice, p.currency)}</Td>
                      <Td className="tabular text-right">
                        <span className={cn(p.available === 0 && 'font-medium text-red-600')}>
                          {formatNumber(p.available)}
                        </span>
                        {p.reserved > 0 && (
                          <span className="block text-xs text-zinc-400">{formatNumber(p.reserved)} reserved</span>
                        )}
                      </Td>
                      <Td>
                        <ActiveBadge isActive={p.isActive} />
                      </Td>
                      <Td className="text-xs whitespace-nowrap text-zinc-500">{formatRelative(p.updatedAt)}</Td>
                    </tr>
                  ))
                )}
              </tbody>
            </Table>
            {data?.items.length === 0 && (
              <EmptyState
                title={filtered ? 'No products match your filters' : 'No products yet'}
                description={
                  filtered
                    ? 'Try a different search term or clear the filters.'
                    : 'Create your first product to start tracking stock.'
                }
              />
            )}
            {data && data.totalCount > 0 && (
              <Pagination
                page={list.page}
                pageSize={PAGE_SIZE}
                totalCount={data.totalCount}
                onPageChange={list.setPage}
                isFetching={isFetching}
              />
            )}
          </>
        )}
      </Card>
    </>
  );
}
