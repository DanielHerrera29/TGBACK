-- Incremento de contactos y vehículos de usuarios. No ejecutar el borrador
-- 20260913_usuarios_escoltas_entregas.sql para este incremento.
-- No crea credenciales, no envía mensajes ni elimina el catálogo legado escoltas.
begin;
alter table public.users add column if not exists whatsapp text;
update public.users set whatsapp='+573144672648' where whatsapp is null or btrim(whatsapp)='';
do $$ begin
 if not exists(select 1 from pg_constraint where conrelid='public.users'::regclass and conname='users_whatsapp_formato') then
  alter table public.users add constraint users_whatsapp_formato check(whatsapp is null or whatsapp ~ '^\+[1-9][0-9]{7,14}$') not valid;
 end if;
end $$;

create table if not exists public.usuario_vehiculos_escolta (
 usuario_id uuid not null references public.users(id) on delete restrict,
 placa text not null check(placa ~ '^[A-Z]{3}[0-9]{3}$'),
 activo boolean not null default true,
 created_at timestamptz not null default now(),
 primary key(usuario_id,placa)
);
-- Una cuenta puede conducir varios vehículos. No se infiere una cuenta por nombre.
do $$ begin
 if not exists(select 1 from public.users where id='6de228a8-daf0-4c57-aafc-52c906035d7a' and upper(email)='WILMER@GMAIL.COM') then
  raise exception 'No coincide el usuario WILMER esperado. No se aplicó la migración.';
 end if;
end $$;
insert into public.usuario_vehiculos_escolta(usuario_id,placa)
select id,p.placa from public.users cross join (values('ABC123'),('MNB124'),('LLO001')) p(placa)
where id='6de228a8-daf0-4c57-aafc-52c906035d7a' and upper(email)='WILMER@GMAIL.COM'
on conflict(usuario_id,placa) do nothing;

create table if not exists public.orden_whatsapp_config (
 id boolean primary key default true check(id),
 destino text not null check(destino ~ '^\+[1-9][0-9]{7,14}$'),
 updated_at timestamptz not null default now()
);
insert into public.orden_whatsapp_config(id,destino) values(true,'+573223509469') on conflict(id) do nothing;
alter table public.usuario_vehiculos_escolta enable row level security;
alter table public.orden_whatsapp_config enable row level security;
revoke all on public.usuario_vehiculos_escolta,public.orden_whatsapp_config from public,anon,authenticated;
grant select,insert,update on public.usuario_vehiculos_escolta,public.orden_whatsapp_config to service_role;
alter table public.ordenes_escolta add column if not exists escolta_usuario_id uuid references public.users(id);

create or replace function public.perfil_usuario_escolta(p_usuario uuid,p_consulta uuid default null)
returns jsonb language plpgsql security definer set search_path=public,pg_temp as $$
declare v_role text; v_id uuid:=coalesce(p_consulta,p_usuario); v_result jsonb;
begin
 select role into v_role from public.users where id=p_usuario and active;
 if v_role is null or v_role not in ('admin','operator','escolta','administrativo','auditor') or (v_id<>p_usuario and v_role<>'admin') then
  raise exception using errcode='42501',message='Acceso denegado';
 end if;
 select jsonb_build_object('id',u.id,'nombre',u.name,'whatsapp',u.whatsapp,'destino',c.destino,
 'placas',coalesce((select jsonb_agg(v.placa order by v.placa) from public.usuario_vehiculos_escolta v where v.usuario_id=u.id and v.activo),'[]'::jsonb))
 into v_result from public.users u cross join public.orden_whatsapp_config c where u.id=v_id;
 return v_result;
end $$;

create or replace function public.editar_vehiculos_usuario(p_admin uuid,p_usuario uuid,p_placas text[])
returns void language plpgsql security definer set search_path=public,pg_temp as $$
begin
 if not exists(select 1 from public.users where id=p_admin and active and role='admin') then raise exception using errcode='42501',message='Acceso denegado'; end if;
 perform 1 from public.users where id=p_usuario for update;
 if not found then raise exception using errcode='22023',message='Usuario inexistente'; end if;
 if p_placas is null or cardinality(p_placas)>30 or exists(select 1 from unnest(p_placas) p where p is null or p !~ '^[A-Z]{3}[0-9]{3}$') then
  raise exception using errcode='22023',message='Placas inválidas';
 end if;
 update public.usuario_vehiculos_escolta set activo=false where usuario_id=p_usuario and not(placa=any(p_placas));
 insert into public.usuario_vehiculos_escolta(usuario_id,placa,activo)
 select p_usuario,p,true from (select distinct unnest(p_placas) p) x
 on conflict(usuario_id,placa) do update set activo=true;
end $$;

create or replace function public.validar_usuario_orden_escolta()
returns trigger language plpgsql security definer set search_path=public,pg_temp as $$
declare v_role text; v_nombre text;
begin
 -- Las órdenes confirmadas anteriores conservan su identidad y su PDF.
 if TG_OP='UPDATE' and old.estado_captura='CONFIRMADA' then return new; end if;
 select role,name into v_role,v_nombre from public.users where id=new.created_by;
 if v_role in ('operator','escolta') then
  new.escolta_usuario_id:=new.created_by;
  new.nombre_escolta:=v_nombre;
  new.placa_escolta:=upper(trim(new.placa_escolta));
  if new.estado_captura='CONFIRMADA' and not exists(select 1 from public.usuario_vehiculos_escolta where usuario_id=new.created_by and placa=new.placa_escolta and activo) then
   raise exception using errcode='22023',message='Seleccione un vehículo asignado a su usuario escolta';
  end if;
 end if;
 return new;
end $$;
drop trigger if exists trg_usuario_orden_escolta on public.ordenes_escolta;
create trigger trg_usuario_orden_escolta before insert or update on public.ordenes_escolta for each row execute function public.validar_usuario_orden_escolta();
revoke all on function public.perfil_usuario_escolta(uuid,uuid),public.editar_vehiculos_usuario(uuid,uuid,text[]),public.validar_usuario_orden_escolta() from public,anon,authenticated;
grant execute on function public.perfil_usuario_escolta(uuid,uuid),public.editar_vehiculos_usuario(uuid,uuid,text[]) to service_role;
commit;

-- Verificación sin claves ni contraseñas:
select u.id,u.name,u.whatsapp,v.placa from public.users u
left join public.usuario_vehiculos_escolta v on v.usuario_id=u.id and v.activo
where u.id='6de228a8-daf0-4c57-aafc-52c906035d7a';
