-- Aplicar después de 20260913_contactos_whatsapp_usuarios.sql.
-- Las órdenes históricas confirmadas conservan su folio/PDF; las NUEVAS
-- confirmaciones reciben código por placa. El bigint anterior queda interno.
begin;
alter table public.ordenes_escolta add column if not exists codigo_orden text,
 add column if not exists numero_placa bigint,
 add column if not exists placa_numeracion text;
create table if not exists public.orden_contadores_placa (
 placa text primary key check(placa ~ '^[A-Z]{3}[0-9]{3}$'),
 prefijo text not null unique,
 ultimo bigint not null check(ultimo>0)
);
create unique index if not exists ordenes_codigo_orden_unique on public.ordenes_escolta(codigo_orden) where codigo_orden is not null;
create unique index if not exists ordenes_numero_por_placa_unique on public.ordenes_escolta(placa_numeracion,numero_placa) where numero_placa is not null;
alter table public.orden_contadores_placa enable row level security;
revoke all on public.orden_contadores_placa from public,anon,authenticated;
grant select on public.orden_contadores_placa to service_role;

create or replace function public.numerar_orden_por_placa()
returns trigger language plpgsql security definer set search_path=public,pg_temp as $$
declare v_placa text; v_numero bigint;
begin
 if TG_OP='UPDATE' and old.codigo_orden is not null then
  if new.codigo_orden is distinct from old.codigo_orden or new.numero_placa is distinct from old.numero_placa
     or new.placa_numeracion is distinct from old.placa_numeracion or new.placa_escolta is distinct from old.placa_escolta then
   raise exception using errcode='22023',message='La numeración de una orden confirmada es inmutable';
  end if;
  return new;
 end if;
 if new.codigo_orden is not null or new.numero_placa is not null or new.placa_numeracion is not null then
  raise exception using errcode='22023',message='El código lo asigna el servidor';
 end if;
 if new.estado_captura<>'CONFIRMADA' then return new; end if;
 if TG_OP='UPDATE' and old.estado_captura='CONFIRMADA' then return new; end if;
 v_placa:=upper(trim(new.placa_escolta));
 if v_placa is null or v_placa !~ '^[A-Z]{3}[0-9]{3}$' then
  raise exception using errcode='22023',message='Seleccione una placa escolta válida para numerar';
 end if;
 begin
  insert into public.orden_contadores_placa as c(placa,prefijo,ultimo) values(v_placa,right(v_placa,4),1)
  on conflict(placa) do update set ultimo=c.ultimo+1 returning ultimo into v_numero;
 exception when unique_violation then
  raise exception using errcode='22023',message='Otra placa tiene el mismo sufijo de numeración. Administración debe resolver la coincidencia';
 end;
 new.placa_escolta:=v_placa;
 new.placa_numeracion:=v_placa;
 new.numero_placa:=v_numero;
 new.codigo_orden:=right(v_placa,4)||'-'||v_numero;
 return new;
end $$;
drop trigger if exists trg_numerar_orden_por_placa on public.ordenes_escolta;
create trigger trg_numerar_orden_por_placa before insert or update on public.ordenes_escolta
 for each row execute function public.numerar_orden_por_placa();
revoke all on function public.numerar_orden_por_placa() from public,anon,authenticated;

-- Alta atómica e idempotente: usuario + cero o varias placas en una transacción.
-- El UUID lo conserva el formulario para recuperar una respuesta perdida.
create or replace function public.crear_usuario_con_placas(p_admin uuid,p_id uuid,p_nombre text,p_email text,p_password text,p_role text,p_whatsapp text,p_placas text[])
returns uuid language plpgsql security definer set search_path=public,pg_temp as $$
declare v_exist public.users;
begin
 if not exists(select 1 from public.users where id=p_admin and active and role='admin') then raise exception using errcode='42501',message='Solo administración puede crear usuarios'; end if;
 if p_id is null or nullif(trim(p_nombre),'') is null or nullif(trim(p_email),'') is null or length(coalesce(p_password,''))<8
 or p_role is null or p_role not in ('admin','operator','auditor') or p_placas is null or cardinality(p_placas)>30
 or exists(select 1 from unnest(p_placas) p where p is null or p !~ '^[A-Z]{3}[0-9]{3}$')
 or (nullif(p_whatsapp,'') is not null and p_whatsapp !~ '^\+[1-9][0-9]{7,14}$') then
  raise exception using errcode='22023',message='Revise los datos de usuario y placas';
 end if;
 perform pg_advisory_xact_lock(hashtextextended('alta_usuario:'||p_id::text,0));
 perform pg_advisory_xact_lock(hashtextextended('alta_email:'||lower(trim(p_email)),0));
 select * into v_exist from public.users where id=p_id;
 if found then
  if v_exist.name is distinct from trim(p_nombre) or v_exist.email is distinct from trim(p_email)
     or v_exist.password is distinct from p_password or v_exist.role is distinct from p_role
     or v_exist.whatsapp is distinct from nullif(p_whatsapp,'')
     or (select coalesce(array_agg(placa order by placa),'{}'::text[]) from public.usuario_vehiculos_escolta where usuario_id=p_id and activo)
       is distinct from (select coalesce(array_agg(p order by p),'{}'::text[]) from (select distinct unnest(p_placas) p) x) then
   raise exception using errcode='P0001',message='La solicitud de alta ya existe con otros datos';
  end if;
  return p_id;
 end if;
 if exists(select 1 from public.users where lower(email)=lower(trim(p_email))) then
  raise exception using errcode='23505',message='Usuario de acceso ya registrado';
 end if;
 insert into public.users(id,name,email,password,role,whatsapp,active)
 values(p_id,trim(p_nombre),trim(p_email),p_password,p_role,nullif(p_whatsapp,''),true);
 insert into public.usuario_vehiculos_escolta(usuario_id,placa)
 select p_id,p from (select distinct unnest(p_placas) p) x;
 return p_id;
end $$;
revoke all on function public.crear_usuario_con_placas(uuid,uuid,text,text,text,text,text,text[]) from public,anon,authenticated;
grant execute on function public.crear_usuario_con_placas(uuid,uuid,text,text,text,text,text,text[]) to service_role;

create or replace function public.catalogo_placas_escolta(p_admin uuid) returns jsonb
language plpgsql security definer set search_path=public,pg_temp as $$
begin
 if not exists(select 1 from public.users where id=p_admin and active and role='admin') then raise exception using errcode='42501',message='Acceso denegado'; end if;
 return (select coalesce(jsonb_agg(placa order by placa),'[]'::jsonb) from (
  select upper(trim(placa)) placa from public.escoltas where activo and upper(trim(placa)) ~ '^[A-Z]{3}[0-9]{3}$'
  union select placa from public.usuario_vehiculos_escolta where activo
 ) x);
end $$;
revoke all on function public.catalogo_placas_escolta(uuid) from public,anon,authenticated;
grant execute on function public.catalogo_placas_escolta(uuid) to service_role;

-- Las definiciones completas de guardar/recuperar se agregan a continuación.
create or replace function public.guardar_orden_servicios(p_usuario uuid, p_clave uuid, p_datos jsonb)
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
 select jsonb_build_object('id',v_orden.id,'consecutivo',v_orden.consecutivo,'codigoOrden',v_orden.codigo_orden,'version',v_orden.version,
   'estado',v_orden.estado_captura,'items',coalesce(jsonb_agg(jsonb_build_object('clientItemId',client_item_id,
   'itemId',id,'servicioId',servicio_id) order by posicion),'[]'::jsonb)) into v_result
 from public.ordenes_escolta_items where orden_id=v_orden.id;
 insert into public.orden_operaciones(usuario_id,clave,solicitud,resultado) values(p_usuario,p_clave,p_datos,v_result);
 return v_result;
end $$;
revoke all on function public.guardar_orden_servicios(uuid,uuid,jsonb) from public,anon,authenticated;
grant execute on function public.guardar_orden_servicios(uuid,uuid,jsonb) to service_role;

create or replace function public.recuperar_orden_servicios(p_usuario uuid,p_client_order uuid)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare o public.ordenes_escolta; v_role text; fields jsonb; items jsonb;
begin
 select role into v_role from public.users where id=p_usuario and active;
 if v_role is null or v_role not in ('admin','operator','administrativo','escolta') then
   raise exception using errcode='42501',message='Acceso denegado';
 end if;
 -- Incluso un administrador solo recupera aquí su propio borrador editable.
 select * into o from public.ordenes_escolta where created_by=p_usuario and client_order_id=p_client_order;
 if not found then return null; end if;
 select coalesce(jsonb_agg(jsonb_build_object('clientItemId',client_item_id,'maquina',coalesce(maquina,''),
   'origen',coalesce(origen,''),'destino',coalesce(destino,'')) order by posicion),'[]'::jsonb),
   coalesce(jsonb_agg(jsonb_build_object('clientItemId',client_item_id,'itemId',id,'servicioId',servicio_id)
   order by posicion),'[]'::jsonb) into fields,items
 from public.ordenes_escolta_items where orden_id=o.id;
 return jsonb_build_object('clientOrderId',o.client_order_id,'version',o.version,
   'fields',jsonb_build_object('fecha',o.fecha,'empresa',coalesce(o.empresa,''),'placaCamabaja',coalesce(o.placa_camabaja,''),
   'placaEscolta',coalesce(o.placa_escolta,''),'nombreEscolta',coalesce(o.nombre_escolta,''),
   'observaciones',coalesce(o.observaciones,''),'clienteId',o.cliente_id,
   'clienteDocumentoSnapshot',o.cliente_documento_snapshot,'vehiculoPlacaSnapshot',o.vehiculo_placa_snapshot,'viajes',fields),
   'result',jsonb_build_object('id',o.id,'version',o.version,'consecutivo',o.consecutivo,'codigoOrden',o.codigo_orden,'estado',o.estado_captura,'items',items));
end $$;
revoke all on function public.recuperar_orden_servicios(uuid,uuid) from public,anon,authenticated;
grant execute on function public.recuperar_orden_servicios(uuid,uuid) to service_role;


commit;

