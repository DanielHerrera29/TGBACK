-- Gestión de clientes: placas de carga independientes de las fichas RNDC.
-- Aplicar después de 20260916_placa_directa_cliente.sql. No elimina datos.
begin;
create table if not exists public.cliente_placas (
 cliente_id uuid not null references public.clientes(id) on delete restrict,
 placa text not null check (placa ~ '^[A-Z]{3}[0-9]{3}$'),
 created_at timestamptz not null default now(),
 primary key(cliente_id,placa)
);
alter table public.cliente_placas enable row level security;
revoke all on public.cliente_placas from public,anon,authenticated;
grant select,insert on public.cliente_placas to service_role;
insert into public.cliente_placas(cliente_id,placa)
 select id,placa_carga from public.clientes where placa_carga is not null
 on conflict do nothing;

create or replace function public.registrar_cliente_placas(p_usuario uuid,p_id uuid,p_tipo text,p_nombre text,p_documento text,p_placas text[])
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare c public.clientes; v_placas text[];
begin
 if not exists(select 1 from public.users where id=p_usuario and active and role in ('admin','administrativo')) then
  raise exception using errcode='42501',message='Solo administración puede gestionar clientes';
 end if;
 if p_id is null or p_tipo is null or p_tipo not in ('empresa','persona') or nullif(trim(p_nombre),'') is null
 or length(trim(p_nombre))>150 or p_documento is null or p_documento !~ '^[0-9]{5,20}$'
 or p_placas is null or cardinality(p_placas)>100
 or exists(select 1 from unnest(p_placas) p where p is null or p !~ '^[A-Z]{3}[0-9]{3}$') then
  raise exception using errcode='22023',message='Revise los datos del cliente y las placas';
 end if;
 select coalesce(array_agg(distinct p order by p),'{}'::text[]) into v_placas from unnest(p_placas) p;
 perform pg_advisory_xact_lock(hashtextextended('cliente:'||p_id::text,0));
 select * into c from public.clientes where id=p_id for update;
 if found then
  if not c.activo or c.nombre is distinct from trim(p_nombre) or c.tipo_cliente is distinct from p_tipo
    or c.nit_o_documento is distinct from p_documento then
   raise exception using errcode='P0001',message='La solicitud ya existe con otros datos';
  end if;
 else
  insert into public.clientes(id,tipo_cliente,nombre,razon_social,nit_o_documento,activo)
  values(p_id,p_tipo,trim(p_nombre),case when p_tipo='empresa' then trim(p_nombre) end,p_documento,true) returning * into c;
 end if;
 -- Inserción idempotente: agregar placas no retira vínculos ni cambia órdenes históricas.
 insert into public.cliente_placas(cliente_id,placa) select p_id,p from unnest(v_placas) p on conflict do nothing;
 return to_jsonb(c)||jsonb_build_object('placas_carga',
   (select coalesce(jsonb_agg(placa order by placa),'[]'::jsonb) from public.cliente_placas where cliente_id=p_id));
end $$;
revoke all on function public.registrar_cliente_placas(uuid,uuid,text,text,text,text[]) from public,anon,authenticated;
grant execute on function public.registrar_cliente_placas(uuid,uuid,text,text,text,text[]) to service_role;

create or replace function public.clientes_para_orden(p_usuario uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare v_result jsonb;
begin
 if not exists(select 1 from public.users where id=p_usuario and active and role in ('admin','operator','administrativo','escolta')) then
  raise exception using errcode='42501',message='Acceso denegado';
 end if;
 select coalesce(jsonb_agg(x order by x.nombre),'[]'::jsonb) into v_result from (
  select c.id,c.tipo_cliente,c.nombre,c.razon_social,c.nit_o_documento,c.activo,c.placa_carga,
   coalesce((select jsonb_agg(cp.placa order by cp.placa) from public.cliente_placas cp where cp.cliente_id=c.id),'[]'::jsonb) as placas_carga,
   coalesce((select jsonb_agg(jsonb_build_object('activo',true,'vehiculos',jsonb_build_object('id',v.id,'num_placa',v.num_placa,'estado',v.estado)) order by v.num_placa)
    from public.cliente_vehiculos cv join public.vehiculos v on v.id=cv.vehiculo_id
    where cv.cliente_id=c.id and cv.activo and v.estado is distinct from 'inactive'),'[]'::jsonb) as cliente_vehiculos
  from public.clientes c where c.activo
 ) x;
 return v_result;
end $$;
revoke all on function public.clientes_para_orden(uuid) from public,anon,authenticated;
grant execute on function public.clientes_para_orden(uuid) to service_role;

create or replace function public.usuarios_para_gestion(p_usuario uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
begin
 if not exists(select 1 from public.users where id=p_usuario and active and role='admin') then
  raise exception using errcode='42501',message='Acceso denegado';
 end if;
 return (select coalesce(jsonb_agg(x order by x.name),'[]'::jsonb) from (
  select id,name,email,role,active,created_at,whatsapp from public.users
 ) x);
end $$;
revoke all on function public.usuarios_para_gestion(uuid) from public,anon,authenticated;
grant execute on function public.usuarios_para_gestion(uuid) to service_role;
commit;
-- Reversión de aplicación: regresar a la versión anterior conserva las placas
-- nuevas en cliente_placas. No eliminar esta tabla ni las funciones con datos.
