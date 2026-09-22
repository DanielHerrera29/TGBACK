-- Incremento 1. Aplicar SOLO tras inventario y respaldo, primero en desarrollo.
-- Requiere las tablas de órdenes, users y clientes del DDL TEG aportado.
-- No modifica datos históricos ni activa acceso directo de Flutter.
begin;
create table public.servicios (
 id uuid primary key default gen_random_uuid(),
 folio bigint generated always as identity unique,
 cliente_id uuid references public.clientes(id) on delete restrict,
 empresa text,
 service_type text check (service_type in ('PROPIO','TERCERO')),
 estado_operativo text not null default 'BORRADOR'
   check (estado_operativo in ('BORRADOR','EN_REVISION')),
 created_by uuid not null references public.users(id),
 created_at timestamptz not null default now(),
 updated_at timestamptz not null default now()
);
create table public.servicio_trayectos (
 servicio_id uuid primary key references public.servicios(id) on delete restrict,
 maquina text, origen text, destino text, placa_camabaja text,
 peso_toneladas numeric(12,3) check (peso_toneladas > 0),
 peso_kg numeric generated always as (peso_toneladas * 1000) stored
);
alter table public.ordenes_escolta
 add column client_order_id uuid,
 add column version integer not null default 1,
 add column estado_captura text not null default 'LEGADO'
   check (estado_captura in ('LEGADO','BORRADOR','CONFIRMADA')),
 add constraint ordenes_client_order_unique unique(created_by, client_order_id);
alter table public.ordenes_escolta_items
 add column client_item_id uuid,
 add column servicio_id uuid references public.servicios(id) on delete restrict,
 add constraint ordenes_client_item_unique unique(orden_id, client_item_id);
create table public.orden_operaciones (
 usuario_id uuid not null references public.users(id),
 clave uuid not null,
 solicitud jsonb not null,
 resultado jsonb not null,
 created_at timestamptz not null default now(),
 primary key(usuario_id, clave)
);
create index servicios_pendientes on public.servicios(estado_operativo, created_at);
create index servicios_creador on public.servicios(created_by);
create index orden_items_servicio on public.ordenes_escolta_items(servicio_id);
alter table public.servicios enable row level security;
alter table public.servicio_trayectos enable row level security;
alter table public.orden_operaciones enable row level security;
revoke all on public.servicios, public.servicio_trayectos, public.orden_operaciones from anon, authenticated;
grant all on public.servicios, public.servicio_trayectos, public.orden_operaciones to service_role;
grant usage, select on sequence public.servicios_folio_seq to service_role;

create function public.guardar_orden_servicios(p_usuario uuid, p_clave uuid, p_datos jsonb)
returns jsonb language plpgsql security definer set search_path = public, pg_temp as $$
declare
 v_role text; v_old public.orden_operaciones; v_orden public.ordenes_escolta;
 v_item jsonb; v_item_id uuid; v_servicio uuid; v_client uuid;
 v_pos integer := 0; v_result jsonb; v_ids uuid[] := '{}';
 v_confirmar boolean := coalesce((p_datos->>'confirmar')::boolean,false);
begin
 select role into v_role from public.users where id=p_usuario and active for share;
 if v_role is null or v_role not in ('admin','operator','administrativo','escolta') then
   raise exception using errcode='42501', message='Acceso denegado';
 end if;
 if p_clave is null or nullif(p_datos->>'clientOrderId','') is null then
   raise exception using errcode='22023', message='Identidad de operación requerida';
 end if;
 -- Serializa incluso creación inicial y reintentos con respuesta perdida.
 perform pg_advisory_xact_lock(hashtextextended(p_usuario::text,0));
 select * into v_old from public.orden_operaciones where usuario_id=p_usuario and clave=p_clave;
 if found then
   if v_old.solicitud <> p_datos then
     raise exception using errcode='P0001', message='Clave reutilizada con otros datos';
   end if;
   return v_old.resultado;
 end if;
 if jsonb_typeof(p_datos->'viajes') is distinct from 'array' then
   raise exception using errcode='22023', message='Viajes inválidos';
 end if;
 if v_confirmar and (coalesce(p_datos->>'empresa','')='' or coalesce(p_datos->>'placaCamabaja','')=''
     or jsonb_array_length(p_datos->'viajes')=0) then
   raise exception using errcode='22023', message='Complete la orden antes de confirmar';
 end if;
 select * into v_orden from public.ordenes_escolta
 where created_by=p_usuario and client_order_id=(p_datos->>'clientOrderId')::uuid for update;
 if found then
   if v_orden.estado_captura <> 'BORRADOR' then
     raise exception using errcode='P0001', message='La orden está confirmada';
   end if;
   if v_orden.version <> coalesce((p_datos->>'version')::integer,0) then
     raise exception using errcode='P0001', message='Versión desactualizada';
   end if;
 else
   if coalesce((p_datos->>'version')::integer,0) <> 0 then
     raise exception using errcode='P0001', message='Orden no encontrada';
   end if;
   insert into public.ordenes_escolta(created_by,client_order_id,estado_captura)
   values(p_usuario,(p_datos->>'clientOrderId')::uuid,'BORRADOR') returning * into v_orden;
   v_orden.version := 0;
 end if;
 -- No quitar/reordenar ítems guardados en este incremento: evita borrar historia.
 -- El cliente permite agregar y completar; retirar requiere flujo posterior explícito.
 for v_item in select value from jsonb_array_elements(p_datos->'viajes') loop
   v_pos := v_pos+1;
   v_client := (v_item->>'clientItemId')::uuid;
   if v_client is null or v_client=any(v_ids) then
     raise exception using errcode='22023', message='Identificador de viaje inválido o repetido';
   end if;
   v_ids := array_append(v_ids,v_client);
   if v_confirmar and (coalesce(trim(v_item->>'maquina'),'')='' or coalesce(trim(v_item->>'origen'),'')=''
       or coalesce(trim(v_item->>'destino'),'')='') then
     raise exception using errcode='22023', message='Complete cada viaje';
   end if;
   select id,servicio_id into v_item_id,v_servicio from public.ordenes_escolta_items
   where orden_id=v_orden.id and client_item_id=v_client and posicion=v_pos;
   if not found then
     insert into public.servicios(created_by) values(p_usuario) returning id into v_servicio;
     insert into public.servicio_trayectos(servicio_id) values(v_servicio);
     insert into public.ordenes_escolta_items(orden_id,posicion,client_item_id,servicio_id)
     values(v_orden.id,v_pos,v_client,v_servicio) returning id into v_item_id;
   end if;
   update public.ordenes_escolta_items set maquina=nullif(trim(v_item->>'maquina'),''),
     origen=nullif(trim(v_item->>'origen'),''), destino=nullif(trim(v_item->>'destino'),'') where id=v_item_id;
   update public.servicios set empresa=nullif(trim(p_datos->>'empresa'),''),
     cliente_id=nullif(p_datos->>'clienteId','')::uuid,
     estado_operativo=case when v_confirmar then 'EN_REVISION' else 'BORRADOR' end,
     updated_at=now() where id=v_servicio;
   update public.servicio_trayectos set maquina=nullif(trim(v_item->>'maquina'),''),
     origen=nullif(trim(v_item->>'origen'),''), destino=nullif(trim(v_item->>'destino'),''),
     placa_camabaja=nullif(upper(trim(p_datos->>'placaCamabaja')),'') where servicio_id=v_servicio;
 end loop;
 if exists(select 1 from public.ordenes_escolta_items where orden_id=v_orden.id and not(client_item_id=any(v_ids))) then
   raise exception using errcode='22023', message='No se pueden retirar viajes guardados';
 end if;
 update public.ordenes_escolta set fecha=coalesce(nullif(p_datos->>'fecha','')::date,current_date),
   empresa=nullif(trim(p_datos->>'empresa'),''), placa_camabaja=nullif(upper(trim(p_datos->>'placaCamabaja')),''),
   placa_escolta=nullif(upper(trim(p_datos->>'placaEscolta')),''), nombre_escolta=nullif(trim(p_datos->>'nombreEscolta'),''),
   observaciones=nullif(trim(p_datos->>'observaciones'),''), cliente_id=nullif(p_datos->>'clienteId','')::uuid,
   cliente_nombre_snapshot=nullif(trim(p_datos->>'empresa'),''), cliente_documento_snapshot=p_datos->>'clienteDocumentoSnapshot',
   vehiculo_placa_snapshot=p_datos->>'vehiculoPlacaSnapshot', version=v_orden.version+1,
   estado_captura=case when v_confirmar then 'CONFIRMADA' else 'BORRADOR' end
 where id=v_orden.id returning * into v_orden;
 select jsonb_build_object('id',v_orden.id,'consecutivo',v_orden.consecutivo,'version',v_orden.version,
   'estado',v_orden.estado_captura,'items',coalesce(jsonb_agg(jsonb_build_object('clientItemId',client_item_id,
   'itemId',id,'servicioId',servicio_id) order by posicion),'[]'::jsonb)) into v_result
 from public.ordenes_escolta_items where orden_id=v_orden.id;
 insert into public.orden_operaciones(usuario_id,clave,solicitud,resultado) values(p_usuario,p_clave,p_datos,v_result);
 return v_result;
end $$;
revoke all on function public.guardar_orden_servicios(uuid,uuid,jsonb) from public,anon,authenticated;
grant execute on function public.guardar_orden_servicios(uuid,uuid,jsonb) to service_role;

create function public.listar_servicios_operativos(p_usuario uuid) returns jsonb
language plpgsql security definer set search_path=public,pg_temp as $$
declare v_role text; v_result jsonb;
begin
 select role into v_role from public.users where id=p_usuario and active;
 if v_role is null or v_role not in ('admin','administrativo','operator','escolta') then
   raise exception using errcode='42501',message='Acceso denegado';
 end if;
 select coalesce(jsonb_agg(x),'[]'::jsonb) into v_result from (
   select s.id,s.folio,s.empresa,s.estado_operativo,t.maquina,t.origen,t.destino
   from public.servicios s join public.servicio_trayectos t on t.servicio_id=s.id
   where v_role in ('admin','administrativo','operator') or s.created_by=p_usuario
   order by s.created_at desc limit 200
 ) x;
 return v_result;
end $$;
revoke all on function public.listar_servicios_operativos(uuid) from public,anon,authenticated;
grant execute on function public.listar_servicios_operativos(uuid) to service_role;
commit;
